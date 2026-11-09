using System.Globalization;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

// 國內報價(SKQuoteLib):連線狀態機與 kline-tw 請求。實測(2026-11-07,SKCOM 2.13.59):EnterMonitorLONG 回 0 → 3001 →
// 約 0.5 秒後 3003(商品基本資料完成,之後才能查);RequestKLineAMByDate 的資料每根一則 OnNotifyKLineData,結束一定有
// OnKLineComplete("##")(每個請求一則,0 筆時也有)。國內是選用的:任何失敗只改 domestic 欄位,海外的 ready 與結束碼不受影響。
// The domestic quote side (SKQuoteLib): its connection state machine and the kline-tw request. Measured 2026-11-07:
// EnterMonitorLONG answers 0, then 3001, then 3003 about 0.5 s later (product data loaded; queries work only after it);
// each bar arrives as one OnNotifyKLineData and every request ends with exactly one OnKLineComplete("##"), even with no
// rows. The domestic side is optional: any failure only changes the domestic fields, never the overseas ready or the
// exit code.
internal sealed partial class ServeHost
{
    private const int DomesticCodeDisconnected = 3002;
    private const int DomesticCodeReady = 3003;

    // 群益代碼上限之外的保險:正常代碼(TX00、MTX00、TM0000)遠短於此。A sanity cap; real codes are far shorter.
    private const int MaxDomesticCodeLength = 32;

    private readonly Queue<string> _domesticDeferred = new();
    private DomesticPhase _domesticPhase = DomesticPhase.Idle;
    private string? _domesticReason;
    private int? _domesticCode;
    private bool _domesticOpened;
    private bool _domesticEntered;
    private int _domesticAttempt;
    private int _domesticGeneration;
    private ITimer? _domesticTimer;
    private DomesticKLineCollector? _domesticCollector;
    private int _staleDomesticCompletes;

    private enum DomesticPhase
    {
        /// <summary>還沒開始(海外還沒 ready)。Not started yet.</summary>
        Idle,

        /// <summary>第一次連線,等 3003;這段期間的 kline-tw 先存著。First connect, waiting for 3003; kline-tw waits.</summary>
        Connecting,

        Ready,

        /// <summary>3002 之後等退避到期。Waiting for the backoff after a 3002.</summary>
        Disconnected,

        /// <summary>重連的 EnterMonitorLONG 已送,等 3003。A reconnect was sent; waiting for 3003.</summary>
        Reconnecting,

        /// <summary>建不起物件、EnterMonitorLONG 被拒或首次 3003 逾時;不再重試。Not available; never retried.</summary>
        Unavailable,
    }

    private void OpenDomestic()
    {
        SkOpenFailure? failure;

        try
        {
            failure = _source.OpenDomestic();
        }
#pragma warning disable CA1031 // 國內是選用的:掛事件之類的任何失敗都只讓國內不可用,不能讓海外跟著停。The domestic side is optional.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failure = new SkOpenFailure(ServeStates.ReasonCreateFailed, exception.HResult);
        }

        if (failure is null)
        {
            _domesticOpened = true;
            return;
        }

        _log.Write(string.Create(CultureInfo.InvariantCulture, $"建立國內報價物件失敗:{failure.Reason}(0x{failure.HResult:X8}),國內 K 線不可用,海外照常。"));
        SetDomestic(DomesticPhase.Unavailable, failure.Reason, null);
    }

    /// <summary>海外第一次 ready 之後呼叫。Called after the overseas side is ready for the first time.</summary>
    private void StartDomestic()
    {
        if (!_domesticOpened || _domesticPhase != DomesticPhase.Idle)
        {
            return;
        }

        int code = _source.EnterDomesticMonitor();

        if (code != 0)
        {
            // 實測沒有證券或期貨權限時這裡就回錯誤;不重試。Without the account permission this answers at once; never retried.
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"國內 EnterMonitorLONG 回 {code}:{_log.Redact(SafeMessage(code))}。國內 K 線不可用,海外照常。"));
            SetDomestic(DomesticPhase.Unavailable, DomesticStates.ReasonEnterRejected, code);
            EmitDomesticStatus();
            return;
        }

        _domesticEntered = true;
        SetDomestic(DomesticPhase.Connecting, null, null);
        _ = ArmDomesticTimer(TimerKind.DomesticReadyDeadline, _timings.DomesticReadyTimeout);
        EmitDomesticStatus();
    }

    private void HandleDomesticConnect(int code)
    {
        switch (code)
        {
            case DomesticCodeReady when _domesticEntered && _domesticPhase != DomesticPhase.Ready:
                // 首次逾時後才到的 3003 也接受:晚到總比不能用好。A 3003 that arrives after the first-time timeout is accepted too.
                CancelDomesticTimer();
                _domesticAttempt = 0;
                ForgetStaleCompletes();
                SetDomestic(DomesticPhase.Ready, null, null);
                _log.Write("國內報價主機就緒(3003)。");

                // 0.3.0:重連後(或斷線期間記下的)訂閱整組重送,索引重新查。After a reconnect the subscriptions are re-sent.
                ResendDomesticSubscriptions();
                EmitDomesticStatus();
                ReleaseDomesticDeferred();
                break;
            case DomesticCodeDisconnected when _domesticPhase is DomesticPhase.Ready or DomesticPhase.Connecting:
                CancelDomesticTimer();
                _domesticAttempt = 0;

                // 斷線後不期待之前請求的 OnKLineComplete 還會來。After a disconnect no earlier completion is expected.
                ForgetStaleCompletes();

                // 0.3.0:索引可能重新配置,舊對照作廢(與海外同一個理由)。Indexes may be reassigned, so the old map is dropped.
                ClearDomesticIndex();
                SetDomestic(DomesticPhase.Disconnected, DomesticStates.ReasonConnectionLost, null);
                EmitDomesticStatus();
                ReleaseDomesticDeferred();
                ScheduleDomesticReconnect();
                break;
            case DomesticCodeDisconnected when _domesticPhase == DomesticPhase.Reconnecting:
                _domesticAttempt++;
                _domesticPhase = DomesticPhase.Disconnected;
                ScheduleDomesticReconnect();
                break;
        }
    }

    private void HandleDomesticTimer(TimerItem timer)
    {
        if (timer.Generation != _domesticGeneration)
        {
            return;
        }

        switch (timer.Kind)
        {
            case TimerKind.DomesticReadyDeadline when _domesticPhase == DomesticPhase.Connecting:
                _log.Write(string.Create(CultureInfo.InvariantCulture, $"{_timings.DomesticReadyTimeout.TotalSeconds:F0} 秒內沒有收到國內 3003,國內 K 線不可用,海外照常。"));
                SetDomestic(DomesticPhase.Unavailable, DomesticStates.ReasonReadyTimeout, null);
                EmitDomesticStatus();
                ReleaseDomesticDeferred();
                break;
            case TimerKind.DomesticReadyDeadline when _domesticPhase == DomesticPhase.Reconnecting:
                _log.Write("國內重連逾時,稍後再試。");
                _domesticAttempt++;
                _domesticPhase = DomesticPhase.Disconnected;
                ScheduleDomesticReconnect();
                break;
            case TimerKind.DomesticReconnectDue when _domesticPhase == DomesticPhase.Disconnected:
                int code = _source.EnterDomesticMonitor();
                LogCode("重新國內 EnterMonitorLONG", code);

                if (code != 0)
                {
                    _domesticAttempt++;
                    ScheduleDomesticReconnect();
                }
                else
                {
                    _domesticPhase = DomesticPhase.Reconnecting;
                    _ = ArmDomesticTimer(TimerKind.DomesticReadyDeadline, _timings.DomesticReadyTimeout);
                }

                break;
        }
    }

    private void ScheduleDomesticReconnect()
    {
        TimeSpan delay = _timings.ReconnectDelay(_domesticAttempt);
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"國內報價主機斷線,{delay.TotalSeconds:F1} 秒後第 {_domesticAttempt + 1} 次重連。"));
        _ = ArmDomesticTimer(TimerKind.DomesticReconnectDue, delay);
    }

    private void LeaveDomestic()
    {
        if (_domesticEntered)
        {
            LogCode("國內 LeaveMonitor", _source.LeaveDomesticMonitor());
        }
    }

    private int ArmDomesticTimer(TimerKind kind, TimeSpan due)
    {
        CancelDomesticTimer();
        int generation = ++_domesticGeneration;
        _domesticTimer = _time.CreateTimer(_ => _inbox.Writer.TryWrite(new TimerItem(kind, generation)), null, due, Timeout.InfiniteTimeSpan);
        return generation;
    }

    private void CancelDomesticTimer()
    {
        _domesticTimer?.Dispose();
        _domesticTimer = null;
        _domesticGeneration++;
    }

    private void ForgetStaleCompletes()
    {
        lock (_eventGate)
        {
            _staleDomesticCompletes = 0;
        }
    }

    private void SetDomestic(DomesticPhase phase, string? reason, int? code)
    {
        _domesticPhase = phase;
        _domesticReason = reason;
        _domesticCode = code;
    }

    /// <summary>首次連線期間存著的 kline-tw 放回主迴圈依序處理。Hands the kline-tw requests held during the first connect back.</summary>
    private void ReleaseDomesticDeferred()
    {
        while (_domesticDeferred.TryDequeue(out string? line))
        {
            _deferred.Enqueue(line);
        }
    }

    private string DomesticWireState() => _domesticPhase switch
    {
        DomesticPhase.Ready => DomesticStates.Ready,
        DomesticPhase.Disconnected or DomesticPhase.Reconnecting => DomesticStates.Disconnected,
        DomesticPhase.Unavailable => DomesticStates.Unavailable,
        _ => DomesticStates.Connecting,
    };

    private string OverseasWireState() => _phase switch
    {
        Phase.Ready => ServeStates.Ready,
        Phase.Disconnected or Phase.Reconnecting => ServeStates.Disconnected,
        _ => ServeStates.Connecting,
    };

    /// <summary>
    /// 海外狀態的 status 事件;國內已開始(或已確定不可用)時一併帶 domestic 欄位。
    /// A status event for the overseas state, carrying the domestic fields once the domestic side has started or failed.
    /// </summary>
    private void WriteOverseasStatus(string state) =>
        _out.WriteStatus(state, _domesticPhase == DomesticPhase.Idle && _optionsPhase == DomesticPhase.Idle ? null : WriteSideFields);

    /// <summary>
    /// 國內狀態改變:再送一次 status 事件,state 照舊是海外目前的狀態(舊版用戶端看到的只是重複的同一個狀態)。
    /// The domestic state changed: another status event whose state is still the current overseas state, so an older
    /// client just sees the same state again.
    /// </summary>
    private void EmitDomesticStatus() => _out.WriteStatus(OverseasWireState(), WriteSideFields);

    /// <summary>
    /// status 事件上國內與海外選擇權的欄位:已開始(或已確定不可用)的那一邊才寫。
    /// The domestic and overseas option members of a status event, written only for a side that has started or failed.
    /// </summary>
    private void WriteSideFields(Utf8JsonWriter writer)
    {
        if (_domesticPhase != DomesticPhase.Idle)
        {
            WriteDomesticFields(writer);
        }

        if (_optionsPhase != DomesticPhase.Idle)
        {
            WriteOverseasOptionsFields(writer);
        }
    }

    private void WriteDomesticFields(Utf8JsonWriter writer)
    {
        writer.WriteString("domestic", DomesticWireState());

        if (_domesticReason is { } reason)
        {
            writer.WriteString("domesticReason", reason);
        }

        if (_domesticCode is { } code)
        {
            writer.WriteNumber("domesticCode", code);
        }
    }

    private async Task HandleDomesticKLineAsync(long id, string line, JsonElement parameters)
    {
        if (!TryReadDomesticKLineParameters(parameters, out string code, out short type, out short session, out string start, out string end))
        {
            _out.WriteError(
                id,
                WireErrorCodes.RequestInvalid,
                "kline-tw 需要 code、type(0 或 4)、session(0 或 1)、start 與 end(yyyyMMdd)。kline-tw needs code, type (0 or 4), session (0 or 1), start and end (yyyyMMdd).");
            return;
        }

        if (_domesticPhase is DomesticPhase.Idle or DomesticPhase.Connecting)
        {
            // 第一次連線還沒有結果(實測約 0.5 秒):等到 3003 或不可用再處理,免得剛啟動就被拒。
            // The first connect has no outcome yet (about 0.5 s as measured): handled once it is ready or unavailable.
            _domesticDeferred.Enqueue(line);
            return;
        }

        if (_domesticPhase != DomesticPhase.Ready)
        {
            WriteDomesticUnavailable(id);
            return;
        }

        DomesticKLineCollector collector = new(code);

        lock (_eventGate)
        {
            _domesticCollector = collector;
        }

        // sOutType = 1(新版格式)、sMinuteNumber = 1,照實測。sOutType 1 (new format) and sMinuteNumber 1, as measured.
        int result = _source.RequestDomesticKLine(code, type, 1, session, start, end, 1);

        if (result != 0)
        {
            lock (_eventGate)
            {
                _domesticCollector = null;
            }

            string message = _log.Redact(SafeMessage(result));
            _out.WriteError(
                id,
                WireErrorCodes.KLineRejected,
                string.Create(CultureInfo.InvariantCulture, $"國內 K 線請求被拒(代碼 {result}):{message}。The domestic K-line request was rejected (code {result})."),
                writer =>
                {
                    writer.WriteNumber("skCode", result);
                    writer.WriteString("skMessage", message);
                    writer.WriteString("symbol", code);
                });
            return;
        }

        bool completed;

        using (CancellationTokenSource delayCancel = new())
        {
            Task delay = Task.Delay(_timings.DomesticKLineTimeout, _time, delayCancel.Token);
            Task finished = await Task.WhenAny(collector.Completed.Task, delay).ConfigureAwait(true);
            completed = finished == collector.Completed.Task;
            await delayCancel.CancelAsync().ConfigureAwait(true);
        }

        string[] lines;

        lock (_eventGate)
        {
            _domesticCollector = null;
            lines = [.. collector.Lines];

            if (!completed && !collector.SwallowedStale)
            {
                // 這個請求的 OnKLineComplete 之後還會來(實測每個請求一定有一則):到時候吃掉,別讓它提早結束下一個請求。
                // 這次若已經吃掉過一則,當成吃到的是自己的,不再累加,避免一則真的遺失時之後每個請求都逾時。
                // This request's completion still comes later (one per request, as measured) and is swallowed then, so
                // it cannot end the next request early. If this request already swallowed one, that is taken as its
                // own and nothing is added, so a truly lost completion cannot make every later request time out.
                _staleDomesticCompletes++;
            }
        }

        if (!completed)
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"國內 K 線 {code} 在 {_timings.DomesticKLineTimeout.TotalSeconds:F0} 秒內沒有結束(已收 {lines.Length} 筆)。"));
            _out.WriteError(
                id,
                WireErrorCodes.KLineTimeout,
                string.Create(CultureInfo.InvariantCulture, $"國內 K 線在 {_timings.DomesticKLineTimeout.TotalSeconds:F0} 秒內沒有收到結束事件。The domestic K-line request did not end within the time limit."),
                writer =>
                {
                    writer.WriteString("symbol", code);
                    writer.WriteNumber("received", lines.Length);
                });
            return;
        }

        _out.WriteResult(id, writer =>
        {
            writer.WriteStartArray("lines");

            foreach (string row in lines)
            {
                writer.WriteStringValue(row);
            }

            writer.WriteEndArray();
        });
    }

    private void WriteDomesticUnavailable(long id)
    {
        string state = DomesticWireState();
        _out.WriteError(
            id,
            WireErrorCodes.DomesticUnavailable,
            $"國內報價目前不可用({state})。The domestic quote service is not available right now ({state}).",
            writer =>
            {
                writer.WriteString("domestic", state);

                if (_domesticReason is { } reason)
                {
                    writer.WriteString("reason", reason);
                }

                if (_domesticCode is { } code)
                {
                    writer.WriteNumber("skCode", code);
                }
            });
    }

    private void OnDomesticKLine(string stockNo, string data)
    {
        lock (_eventGate)
        {
            DomesticKLineCollector? collector = _domesticCollector;

            // 只收這次要的代碼;其他代碼的行是之前請求的尾巴。Only rows for the code asked this time.
            if (collector is null || !string.Equals(stockNo, collector.Code, StringComparison.Ordinal))
            {
                return;
            }

            collector.Lines.Add(data);
        }
    }

    private void OnDomesticKLineComplete(string end)
    {
        lock (_eventGate)
        {
            DomesticKLineCollector? collector = _domesticCollector;

            if (_staleDomesticCompletes > 0)
            {
                // 逾時請求遲到的結束事件。A late completion of a request that timed out.
                _staleDomesticCompletes--;

                if (collector is not null)
                {
                    collector.SwallowedStale = true;
                }

                return;
            }

            _ = collector?.Completed.TrySetResult();
        }
    }

    /// <summary>
    /// 國內商品代碼:非空、不含逗號、<c>#</c>、空白或控制字元(例如 <c>TX00</c>、<c>MTX00</c>、<c>TM0000</c>)。
    /// A domestic code: non-empty, without commas, '#', whitespace or control characters.
    /// </summary>
    internal static bool IsValidDomesticCode(string code)
    {
        if (code.Length is 0 or > MaxDomesticCodeLength)
        {
            return false;
        }

        foreach (char c in code)
        {
            if (c is ',' or '#' || char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadDomesticKLineParameters(
        JsonElement parameters,
        out string code,
        out short type,
        out short session,
        out string start,
        out string end)
    {
        code = string.Empty;
        type = 0;
        session = 0;
        start = string.Empty;
        end = string.Empty;

        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!parameters.TryGetProperty("code", out JsonElement codeElement)
            || codeElement.ValueKind != JsonValueKind.String
            || !IsValidDomesticCode(codeElement.GetString() ?? string.Empty))
        {
            return false;
        }

        // sKLineType:0 分線、4 完整日線(只有這兩種實測過)。Only 0 (minute) and 4 (full daily) were measured.
        if (!parameters.TryGetProperty("type", out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.Number
            || !typeElement.TryGetInt16(out type)
            || type is not (0 or 4))
        {
            return false;
        }

        if (!parameters.TryGetProperty("session", out JsonElement sessionElement)
            || sessionElement.ValueKind != JsonValueKind.Number
            || !sessionElement.TryGetInt16(out session)
            || session is not (0 or 1))
        {
            return false;
        }

        if (!TryReadDate(parameters, "start", out start) || !TryReadDate(parameters, "end", out end))
        {
            return false;
        }

        code = codeElement.GetString()!;
        return true;
    }

    private sealed class DomesticKLineCollector
    {
        public DomesticKLineCollector(string code)
        {
            Code = code;
        }

        public string Code { get; }

        public List<string> Lines { get; } = [];

        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>這個請求期間吃掉過一則遲到的結束事件。A late completion was swallowed during this request.</summary>
        public bool SwallowedStale { get; set; }
    }
}
