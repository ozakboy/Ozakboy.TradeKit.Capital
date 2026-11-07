using System.Globalization;
using System.Threading.Channels;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// <c>serve</c> 模式的協定層:啟動流程(建立 SKCOM → 登入 → 連線 → ready)、依序處理 stdin 的請求、把報價與成交轉成
/// stdout 事件、3002 斷線後退避重連並重送訂閱。只認 <see cref="ISkQuoteSource"/>,不碰 COM。
/// The protocol layer of the serve mode: the start-up flow (create SKCOM, log in, connect, ready), requests from stdin
/// handled one at a time, quotes and ticks turned into stdout events, and backoff reconnection with the subscriptions
/// re-sent after a 3002 disconnect. It only knows <see cref="ISkQuoteSource"/> and never touches COM.
/// </summary>
/// <remarks>
/// <para>
/// 所有「會改狀態」的工作(請求、連線事件、計時器)都進同一個收件匣,由 <see cref="RunAsync"/> 一次處理一件;真宿主裡
/// <see cref="RunAsync"/> 跑在 STA 執行緒上,所以 SKCOM 的呼叫都在 STA。報價、成交、商品、K 線事件則在觸發它的執行緒上
/// 直接處理(只讀索引快取、寫 stdout),以鎖保護共用資料。
/// Every state-changing piece of work (requests, connection events, timers) goes into one inbox that
/// <see cref="RunAsync"/> drains one item at a time; in the real host <see cref="RunAsync"/> runs on the STA thread, so
/// every SKCOM call happens there. Quote, tick, product and K-line events are handled on the thread that raised them
/// (they only touch the index cache and stdout), with locks around shared data.
/// </para>
/// <para>
/// <b>登入絕不重試。</b>連線類錯誤(3002)才退避重連,重連只呼叫 EnterMonitorLONG,不重新登入。
/// <b>Login is never retried.</b> Only connection errors (3002) back off and reconnect, and a reconnect only calls
/// EnterMonitorLONG, never Login.
/// </para>
/// </remarks>
internal sealed partial class ServeHost
{
    private const int CodeConnected = 3001;
    private const int CodeDisconnected = 3002;

    private readonly ISkQuoteSource _source;
    private readonly ProtocolWriter _out;
    private readonly HostLog _log;
    private readonly ServeTimings _timings;
    private readonly TimeProvider _time;
    private readonly Channel<InboxItem> _inbox = Channel.CreateUnbounded<InboxItem>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Queue<string> _deferred = new();

    private Phase _phase = Phase.Starting;
    private int _attempt;
    private int _generation;
    private ITimer? _timer;

    /// <summary>
    /// 建立協定層。
    /// Creates the protocol layer.
    /// </summary>
    /// <param name="source">SKCOM(或假實作)。SKCOM or a fake.</param>
    /// <param name="output">stdout 協定輸出。The stdout protocol output.</param>
    /// <param name="log">stderr 紀錄。The stderr log.</param>
    /// <param name="timings">時限。Timings.</param>
    /// <param name="time">時間來源。The time source.</param>
    public ServeHost(ISkQuoteSource source, ProtocolWriter output, HostLog log, ServeTimings timings, TimeProvider time)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _out = output ?? throw new ArgumentNullException(nameof(output));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _timings = timings ?? throw new ArgumentNullException(nameof(timings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    private enum Phase
    {
        Starting,
        Ready,
        Disconnected,
        Reconnecting,
    }

    private enum TimerKind
    {
        ConnectDeadline,
        ReconnectDue,
        DomesticReadyDeadline,
        DomesticReconnectDue,
    }

    /// <summary>
    /// 收到一行請求(由讀 stdin 的執行緒呼叫)。
    /// One request line arrived; called by the stdin reader thread.
    /// </summary>
    /// <param name="line">請求。The request line.</param>
    public void EnqueueRequest(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            _ = _inbox.Writer.TryWrite(new RequestItem(line));
        }
    }

    /// <summary>
    /// stdin 結束了(父行程關閉了輸入或已結束):離開報價主機後以 0 結束。
    /// stdin ended (the parent closed it or exited): leave the quote server and exit with 0.
    /// </summary>
    public void CompleteInput() => _inbox.Writer.TryWrite(new InputEndedItem());

    /// <summary>
    /// 跑完整個 serve 流程,回傳結束碼(見 <see cref="ServeExitCodes"/>)。
    /// Runs the whole serve flow and returns the exit code (see <see cref="ServeExitCodes"/>).
    /// </summary>
    /// <param name="login">帳密與紀錄目錄。The login data.</param>
    /// <param name="cancellationToken">取消權杖(測試用)。Cancellation token, for tests.</param>
    /// <returns>結束碼。The exit code.</returns>
    public async Task<int> RunAsync(ServeLogin login, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);

        // 缺帳密:絕不建立 SKCOM、絕不呼叫 Login。Missing credentials: never create SKCOM, never call Login.
        if (!login.IsComplete)
        {
            _log.Write("缺少 CAPITAL_LOGIN_ID 或 CAPITAL_PASSWORD 環境變數,不登入,結束。");
            _out.WriteStatus(ServeStates.Faulted, writer => writer.WriteString("reason", ServeStates.ReasonMissingCredentials));
            return ServeExitCodes.MissingCredentials;
        }

        _log.AddSecret(login.LoginId, "<ID>");
        _log.AddSecret(login.Password, "<PASSWORD>");

        HookEvents();

        SkOpenFailure? failure = _source.Open();

        if (failure is not null)
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"建立 SKCOM 物件失敗:{failure.Reason}(0x{failure.HResult:X8})。"));
            _out.WriteStatus(ServeStates.Faulted, writer =>
            {
                writer.WriteString("reason", failure.Reason);
                writer.WriteString("hresult", string.Create(CultureInfo.InvariantCulture, $"0x{failure.HResult:X8}"));
            });
            return ServeExitCodes.SkComUnavailable;
        }

        // 國內報價是選用的:建不起來只讓國內不可用,海外照常。The domestic side is optional; failing only disables it.
        OpenDomestic();

        if (login.LogDirectory is { } logDirectory)
        {
            LogCode("SetLogPath", _source.SetLogPath(logDirectory));
        }

        _out.WriteStatus(ServeStates.LoggingIn);
        int loginCode = _source.Login(login.LoginId, login.Password);

        if (loginCode != 0)
        {
            string message = _log.Redact(SafeMessage(loginCode));
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"登入失敗(代碼 {loginCode}):{message}。帳密類錯誤不重試。"));
            _log.Write("最後紀錄:" + SafeLastLog());
            _out.WriteStatus(ServeStates.LoginFailed, writer =>
            {
                writer.WriteNumber("code", loginCode);
                writer.WriteString("message", message);
            });
            return ServeExitCodes.LoginFailed;
        }

        _out.WriteStatus(ServeStates.Connecting);
        int enterCode = _source.EnterMonitor();

        if (enterCode != 0)
        {
            string message = _log.Redact(SafeMessage(enterCode));
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"EnterMonitorLONG 回 {enterCode}:{message}。"));
            _out.WriteStatus(ServeStates.Faulted, writer =>
            {
                writer.WriteString("reason", ServeStates.ReasonConnectRejected);
                writer.WriteNumber("code", enterCode);
                writer.WriteString("message", message);
            });
            return ServeExitCodes.ConnectFailed;
        }

        bool? connected = await WaitForInitialConnectAsync(cancellationToken).ConfigureAwait(true);

        if (connected is null)
        {
            _ = _source.LeaveMonitor();
            return ServeExitCodes.Success;
        }

        if (connected == false)
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"{_timings.ConnectTimeout.TotalSeconds:F0} 秒內沒有收到 OnConnect(3001)。"));
            _out.WriteStatus(ServeStates.Faulted, writer => writer.WriteString("reason", ServeStates.ReasonConnectTimeout));
            return ServeExitCodes.ConnectFailed;
        }

        _phase = Phase.Ready;
        WriteOverseasStatus(ServeStates.Ready);

        // 海外就緒後才連國內;國內的結果只改 domestic 欄位,不影響海外的 ready 與結束碼。
        // The domestic side connects only after the overseas side is ready; its outcome only changes the domestic fields.
        StartDomestic();

        return await MainLoopAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task<bool?> WaitForInitialConnectAsync(CancellationToken cancellationToken)
    {
        int generation = ArmTimer(TimerKind.ConnectDeadline, _timings.ConnectTimeout);

        while (true)
        {
            InboxItem item = await _inbox.Reader.ReadAsync(cancellationToken).ConfigureAwait(true);

            switch (item)
            {
                case ConnectItem { Code: CodeConnected }:
                    CancelTimer();
                    return true;
                case ConnectItem:
                    // 啟動中的其他代碼(含 3002)不算結束,繼續等到時限。Other codes during start-up keep waiting until the deadline.
                    break;
                case TimerItem timer when timer.Generation == generation && timer.Kind == TimerKind.ConnectDeadline:
                    return false;
                case RequestItem request:
                    // 還沒 ready 的請求先存著,ready 後依序處理。Requests before ready wait and are handled in order afterwards.
                    _deferred.Enqueue(request.Line);
                    break;
                case InputEndedItem:
                    CancelTimer();
                    return null;
            }
        }
    }

    private async Task<int> MainLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            InboxItem item = _deferred.TryDequeue(out string? deferred)
                ? new RequestItem(deferred)
                : await _inbox.Reader.ReadAsync(cancellationToken).ConfigureAwait(true);

            switch (item)
            {
                case RequestItem request:
                    if (await HandleRequestAsync(request.Line).ConfigureAwait(true))
                    {
                        CancelTimer();
                        CancelDomesticTimer();
                        return ServeExitCodes.Success;
                    }

                    break;
                case InputEndedItem:
                    _log.Write("stdin 已關閉,離開報價主機後結束。");
                    CancelTimer();
                    CancelDomesticTimer();
                    LeaveDomestic();
                    _ = _source.LeaveMonitor();
                    return ServeExitCodes.Success;
                case ConnectItem connect:
                    HandleConnect(connect.Code);
                    break;
                case DomesticConnectItem domestic:
                    HandleDomesticConnect(domestic.Code);
                    break;
                case TimerItem { Kind: TimerKind.DomesticReadyDeadline or TimerKind.DomesticReconnectDue } timer:
                    HandleDomesticTimer(timer);
                    break;
                case TimerItem timer:
                    HandleTimer(timer);
                    break;
            }
        }
    }

    private void HandleConnect(int code)
    {
        switch (_phase)
        {
            case Phase.Ready when code == CodeDisconnected:
                _phase = Phase.Disconnected;
                _attempt = 0;
                WriteOverseasStatus(ServeStates.Disconnected);
                ScheduleReconnect();
                break;
            case Phase.Disconnected or Phase.Reconnecting when code == CodeConnected:
                OnReconnected();
                break;
            case Phase.Reconnecting when code == CodeDisconnected:
                _attempt++;
                _phase = Phase.Disconnected;
                ScheduleReconnect();
                break;
        }
    }

    private void HandleTimer(TimerItem timer)
    {
        if (timer.Generation != _generation)
        {
            return;
        }

        switch (timer.Kind)
        {
            case TimerKind.ReconnectDue when _phase == Phase.Disconnected:
                int code = _source.EnterMonitor();
                LogCode("重新 EnterMonitorLONG", code);

                if (code != 0)
                {
                    _attempt++;
                    ScheduleReconnect();
                }
                else
                {
                    _phase = Phase.Reconnecting;
                    _ = ArmTimer(TimerKind.ConnectDeadline, _timings.ConnectTimeout);
                }

                break;
            case TimerKind.ConnectDeadline when _phase == Phase.Reconnecting:
                _log.Write("重連逾時,稍後再試。");
                _attempt++;
                _phase = Phase.Disconnected;
                ScheduleReconnect();
                break;
        }
    }

    private void ScheduleReconnect()
    {
        TimeSpan delay = _timings.ReconnectDelay(_attempt);
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"報價主機斷線,{delay.TotalSeconds:F1} 秒後第 {_attempt + 1} 次重連。"));
        _ = ArmTimer(TimerKind.ReconnectDue, delay);
    }

    private void OnReconnected()
    {
        CancelTimer();

        // 斷線後索引可能被重新配置,舊對照一律作廢,避免價格掛到別的商品上。
        // Indexes may be reassigned after a disconnect, so the old map is dropped to keep prices off the wrong symbol.
        ClearIndexCache();
        ResendSubscriptions();
        _phase = Phase.Ready;
        _attempt = 0;
        _log.Write("報價主機已重新連線,訂閱已重送。");
        WriteOverseasStatus(ServeStates.Ready);
    }

    private int ArmTimer(TimerKind kind, TimeSpan due)
    {
        CancelTimer();
        int generation = ++_generation;
        _timer = _time.CreateTimer(_ => _inbox.Writer.TryWrite(new TimerItem(kind, generation)), null, due, Timeout.InfiniteTimeSpan);
        return generation;
    }

    private void CancelTimer()
    {
        _timer?.Dispose();
        _timer = null;
        _generation++;
    }

    private void LogCode(string step, int code)
    {
        if (code != 0)
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"{step} 回 {code}:{_log.Redact(SafeMessage(code))}"));
        }
    }

    private string SafeMessage(int code)
    {
        try
        {
            return _source.GetReturnCodeMessage(code) ?? string.Empty;
        }
#pragma warning disable CA1031 // 只是取說明文字:失敗就用空字串,不能讓它中斷流程。Only a description; a failure must not stop the flow.
        catch (Exception)
        {
            return string.Empty;
        }
#pragma warning restore CA1031
    }

    private string SafeLastLog()
    {
        try
        {
            return _log.Redact(_source.GetLastLogInfo());
        }
#pragma warning disable CA1031 // 同上。Same as above.
        catch (Exception)
        {
            return string.Empty;
        }
#pragma warning restore CA1031
    }

    private abstract record InboxItem;

    private sealed record RequestItem(string Line) : InboxItem;

    private sealed record InputEndedItem : InboxItem;

    private sealed record ConnectItem(int Code) : InboxItem;

    private sealed record DomesticConnectItem(int Code) : InboxItem;

    private sealed record TimerItem(TimerKind Kind, int Generation) : InboxItem;
}
