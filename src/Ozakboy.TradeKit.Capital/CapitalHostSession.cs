using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ozakboy.Security.Masking;
using Ozakboy.TradeKit.Capital.Protocol;

namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 群益 SKCOM 海外報價的工作階段:啟動並監督宿主子行程(<c>Ozakboy.TradeKit.Capital.Host serve</c>)、送請求、讀報價與成交。
/// 0.2.0 起另可查國內期貨歷史 K 線(<see cref="GetDomesticKLinesAsync"/>),國內狀態獨立於海外(<see cref="DomesticState"/>)。
/// A Capital SKCOM overseas quote session: starts and supervises the host child process, sends requests, and reads
/// quotes and ticks. Since 0.2.0 it also fetches domestic futures historical K-lines, with a domestic state kept
/// separate from the overseas one.
/// </summary>
/// <remarks>
/// <para>
/// <b>帳密只走子行程的環境變數</b>(<c>CAPITAL_LOGIN_ID</c>、<c>CAPITAL_PASSWORD</c>),不寫檔、不進日誌;宿主讀完立刻從自己的環境清掉。
/// 帳密一拿到就登記到遮罩器,宿主的 stderr 逐行遮罩之後才寫日誌;另外不論長短,帳密的字面值一律換成 <c>&lt;ID&gt;</c> /
/// <c>***</c>(遮罩器不收短於 8 個字元的值,所以多這一道)。
/// <b>Credentials travel only through the child's environment</b>, never into a file or a log; the host clears them
/// right after reading. They are registered on the masker as soon as they arrive and the host's stderr is masked line
/// by line before logging; their literal values are also replaced whatever their length, because the masker refuses
/// values shorter than 8 characters.
/// </para>
/// <para>
/// <b>登入絕不重試。</b>登入失敗(宿主結束碼 4)進入 <see cref="CapitalHostState.LoginFailed"/> 後就停,錯太多次群益會鎖帳號;
/// SKCOM 不可用(結束碼 3)與缺帳密(結束碼 2)進入 <see cref="CapitalHostState.Faulted"/>。其他意外結束以 1、2、5、10、30 秒退避
/// 重啟(最多 <see cref="CapitalHostOptions.MaxRestarts"/> 次,重啟後的行程穩定跑 5 分鐘計數歸零),重啟後重送全部訂閱。
/// 報價主機斷線(3002)由宿主自己退避重連,這裡只看到 <see cref="CapitalHostState.Disconnected"/> → <see cref="CapitalHostState.Ready"/>。
/// <b>Login is never retried.</b> A failed login (host exit code 4) stops in LoginFailed; SKCOM being unavailable (3)
/// and missing credentials (2) stop in Faulted. Any other unexpected exit is restarted with 1, 2, 5, 10 and 30 second
/// backoff up to MaxRestarts (the count starts over after 5 minutes of stable running) and every subscription is
/// re-sent. A quote-server disconnect (3002) is reconnected by the host itself; this type only sees Disconnected then
/// Ready.
/// </para>
/// </remarks>
public sealed partial class CapitalHostSession : IAsyncDisposable
{
    private static readonly TimeSpan ProductsTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan KLineTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan ShortRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly CapitalHostOptions _options;
    private readonly ILogger _logger;
    private readonly SecretMasker _masker;
    private readonly TimeProvider _time;
    private readonly Channel<CapitalMarketEvent> _events;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _stateLock = new();
    private readonly List<CapitalSymbol> _subscriptions = [];
    private readonly ConcurrentDictionary<string, byte> _warnedOnce = new(StringComparer.Ordinal);

    private CapitalHostState _state = CapitalHostState.Stopped;
    private CapitalHostProcess? _host;
    private CapitalCredentials? _credentials;
    private CancellationTokenSource? _supervisorSource;
    private Task? _supervisorTask;
    private long _droppedEvents;
    private bool _disposed;

    // 宿主行程的 Job Object(Windows,KILL_ON_JOB_CLOSE):持有到 DisposeAsync,父行程消失時系統會把宿主一起收掉。
    // The hosts' Job Object (Windows, kill on close), held until DisposeAsync so the system ends the host with the parent.
    private CapitalHostJob? _job;
    private bool _jobFailed;

    /// <summary>
    /// 建立工作階段(不啟動任何行程)。
    /// Creates the session without starting anything.
    /// </summary>
    /// <param name="options">設定。The settings.</param>
    /// <param name="logger">日誌;<see langword="null"/> 時不寫。The logger, or none.</param>
    /// <param name="masker">遮罩器;<see langword="null"/> 時用 <see cref="SecretMasker.Default"/>。The masker.</param>
    /// <param name="timeProvider">時間來源;<see langword="null"/> 時用系統時間。The time source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 為 null。<paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">設定不合法。The settings are invalid.</exception>
    public CapitalHostSession(
        CapitalHostOptions options,
        ILogger<CapitalHostSession>? logger = null,
        SecretMasker? masker = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        Result validation = options.Validate();

        if (validation.IsFailure)
        {
            throw new ArgumentException(validation.Error.Message, nameof(options));
        }

        _options = options;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _masker = masker ?? SecretMasker.Default;
        _time = timeProvider ?? TimeProvider.System;
        _events = Channel.CreateBounded<CapitalMarketEvent>(
            new BoundedChannelOptions(options.EventCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = false,
            },
            _ => Interlocked.Increment(ref _droppedEvents));
    }

    /// <summary>
    /// 狀態改變(在觸發改變的執行緒上同步呼叫;處理常式不要阻塞)。
    /// Raised synchronously on the thread that changed the state; handlers must not block.
    /// </summary>
    public event EventHandler<CapitalHostStateChangedEventArgs>? StateChanged;

    /// <summary>目前狀態。The current state.</summary>
    public CapitalHostState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// 報價與成交(<see cref="CapitalQuote"/> / <see cref="CapitalTick"/>)。有界佇列,滿了丟最舊的並計入 <see cref="DroppedEvents"/>;
    /// 跨重啟沿用同一個佇列,<see cref="DisposeAsync"/> 時完成。
    /// Quotes and ticks. A bounded queue that drops the oldest when full and counts it in <see cref="DroppedEvents"/>;
    /// the same queue survives restarts and completes on <see cref="DisposeAsync"/>.
    /// </summary>
    public ChannelReader<CapitalMarketEvent> Events => _events.Reader;

    /// <summary>因佇列滿而丟掉的事件數。Events dropped because the queue was full.</summary>
    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);

    /// <summary>
    /// 目前的訂閱(用戶端記住的集合,重啟後重送用)。
    /// The current subscriptions, as remembered by the client for re-sending after a restart.
    /// </summary>
    public IReadOnlyList<CapitalSymbol> Subscriptions
    {
        get
        {
            lock (_stateLock)
            {
                return [.. _subscriptions];
            }
        }
    }

    /// <summary>
    /// 啟動宿主並等到 ready。登入失敗回 <see cref="CapitalErrorCodes.LoginFailed"/> 且<b>不重啟</b>;SKCOM 不可用回
    /// <see cref="CapitalErrorCodes.SkComNotRegistered"/> / <see cref="CapitalErrorCodes.SkComFileMissing"/>;連不上回
    /// <see cref="CapitalErrorCodes.ConnectTimeout"/>。已經 Ready 或 Disconnected 時直接成功。
    /// Starts the host and waits for ready. A failed login is <see cref="CapitalErrorCodes.LoginFailed"/> and is
    /// <b>not restarted</b>; SKCOM being unavailable is not_registered or file_missing; an unreachable server is
    /// connect.timeout. Already Ready or Disconnected succeeds at once.
    /// </summary>
    /// <param name="credentials">帳密。The credentials.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>就緒時為成功。Success once ready.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="credentials"/> 為 null。</exception>
    /// <exception cref="ObjectDisposedException">已釋放。Disposed.</exception>
    public async Task<Result> StartAsync(CapitalCredentials credentials, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(credentials);

        Result valid = credentials.Validate();

        if (valid.IsFailure)
        {
            return valid;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CapitalErrors.RequestCancelled("start");
        }

        try
        {
            switch (State)
            {
                case CapitalHostState.Ready or CapitalHostState.Disconnected:
                    return Result.Success();
                case CapitalHostState.Starting or CapitalHostState.LoggingIn or CapitalHostState.Connecting:
                    return CapitalErrors.InvalidState("群益宿主正在啟動或重啟中。The Capital host is already starting or restarting.");
            }

            if (!File.Exists(_options.HostExecutablePath))
            {
                // 什麼都沒啟動,狀態不動。Nothing was started, so the state stays as it is.
                return CapitalErrors.HostNotFound(_options.HostExecutablePath);
            }

            RegisterSecrets(credentials);
            _credentials = credentials;
            SetState(CapitalHostState.Starting);

            Result launched = await LaunchAsync(cancellationToken).ConfigureAwait(false);

            if (launched.IsFailure)
            {
                _credentials = null;
                SetState(StateForFailure(launched.Error, cancellationToken));
                return launched;
            }

            SetState(CapitalHostState.Ready);
            StartSupervisor();
            return Result.Success();
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <summary>
    /// 停止:送 shutdown(宿主 LeaveMonitor 後以 0 結束),逾時就終止。清掉記憶中的帳密與訂閱。
    /// Stops: sends shutdown (the host leaves the monitor and exits 0), killing it on timeout. The remembered
    /// credentials and subscriptions are cleared.
    /// </summary>
    /// <param name="cancellationToken">取消權杖(只影響等待 gate)。Cancels only the wait for the gate.</param>
    /// <returns>成功。Success.</returns>
    public async Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        Task? supervisor = await CancelSupervisorAsync().ConfigureAwait(false);

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CapitalErrors.RequestCancelled("stop");
        }

        try
        {
            supervisor ??= await CancelSupervisorAsync().ConfigureAwait(false);

            if (supervisor is not null)
            {
                try
                {
                    await supervisor.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 監督因取消結束是預期的。The supervisor ending by cancellation is expected.
                }
            }

            CapitalHostProcess? host;

            lock (_stateLock)
            {
                host = _host;
                _host = null;
                _supervisorSource?.Dispose();
                _supervisorSource = null;
                _supervisorTask = null;
                _subscriptions.Clear();
                ClearLiveSubscriptions();
            }

            if (host is not null)
            {
                if (await host.ShutdownAsync(ShutdownTimeout).ConfigureAwait(false))
                {
                    CapitalHostLog.Killed(_logger);
                }

                await host.DisposeAsync().ConfigureAwait(false);
            }

            _credentials = null;
            SetDomesticState(CapitalDomesticState.NotStarted, null, null);
            SetOverseasOptionsState(CapitalOverseasOptionsState.NotStarted, null, null);
            SetState(CapitalHostState.Stopped);
            return Result.Success();
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <summary>
    /// 停止並釋放;<see cref="Events"/> 完成。
    /// Stops and releases resources; <see cref="Events"/> completes.
    /// </summary>
    /// <returns>代表非同步作業的工作。A task for the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _ = _events.Writer.TryComplete();
        _gate.Dispose();

        CapitalHostJob? job;

        lock (_stateLock)
        {
            job = _job;
            _job = null;
        }

        // 關掉 job handle:萬一還有宿主沒結束,系統會收掉它。Closing the job ends any host still running.
        if (job is not null && OperatingSystem.IsWindows())
        {
            job.Dispose();
        }
    }

    /// <summary>目前宿主的行程識別碼(測試用)。The current host's process id, for tests.</summary>
    internal int? HostProcessId
    {
        get
        {
            lock (_stateLock)
            {
                return _host?.ProcessId;
            }
        }
    }

    /// <summary>目前宿主是否在工作階段的 Job Object 裡(測試用)。Whether the current host is in the session's job, for tests.</summary>
    internal bool HostIsInJob()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        CapitalHostProcess? host;
        CapitalHostJob? job;

        lock (_stateLock)
        {
            host = _host;
            job = _job;
        }

        return host is not null && job is not null && host.IsIn(job);
    }

    /// <summary>
    /// 海外商品清單(清單併明細;宿主各等 60 秒的 <c>##</c> 結尾)。
    /// The overseas product list, merged with the details.
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>商品清單。The products.</returns>
    public async Task<Result<IReadOnlyList<CapitalProduct>>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        Result<JsonElement> response = await SendAsync("products", null, ProductsTimeout, cancellationToken).ConfigureAwait(false);
        return response.IsFailure ? response.Error : CapitalWire.ParseProducts(response.GetValueOrThrow());
    }

    /// <summary>
    /// 訂報價與成交(與已訂集合取聯集)。訂閱當下群益會先回補當個交易日的全部成交(<see cref="CapitalTick.IsReplay"/>),再接即時。
    /// Subscribes quotes and ticks, merged with what is already subscribed. Capital first replays the whole trading
    /// day's ticks, then sends live ones.
    /// </summary>
    /// <param name="symbols">商品。The symbols.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>宿主目前的完整訂閱集合。The host's full subscription set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="symbols"/> 為 null。</exception>
    public async Task<Result<IReadOnlyList<CapitalSymbol>>> SubscribeAsync(IEnumerable<CapitalSymbol> symbols, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        List<CapitalSymbol> requested = [];

        foreach (CapitalSymbol symbol in symbols)
        {
            if (!symbol.IsValid)
            {
                return CapitalErrors.InvalidArgument("商品代碼不合法(交易所與代碼都要有值,不可含逗號、# 或空白)。A symbol is invalid.");
            }

            if (!requested.Contains(symbol))
            {
                requested.Add(symbol);
            }
        }

        if (requested.Count == 0)
        {
            return CapitalErrors.InvalidArgument("至少要一個商品。At least one symbol is required.");
        }

        Result<IReadOnlyList<CapitalSymbol>> result = await SendSubscribeAsync(requested, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            lock (_stateLock)
            {
                foreach (CapitalSymbol symbol in requested)
                {
                    if (!_subscriptions.Contains(symbol))
                    {
                        _subscriptions.Add(symbol);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 歷史 K 線(<c>RequestKLineByDate</c>)。分 K 的 <see cref="CapitalBar.OpenTimeUtc"/> 已依交易所時區轉 UTC 並減 1 分鐘;
    /// 價格依 <paramref name="decimals"/> 四捨五入。只到上一個交易日(實測當日進行中的不給)。
    /// Historical K-lines. A minute bar's open time is converted to UTC by the exchange's zone and moved back one minute;
    /// prices are rounded to <paramref name="decimals"/>. Data ends at the previous trading day, as measured.
    /// </summary>
    /// <param name="symbol">商品。The symbol.</param>
    /// <param name="type">K 線類型。The bar type.</param>
    /// <param name="from">起日(含)。The first date, inclusive.</param>
    /// <param name="to">迄日(含)。The last date, inclusive.</param>
    /// <param name="decimals">價格小數位數(報價的 <see cref="CapitalQuote.Decimals"/>)。The price decimals.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>K 線(可能是空的)。The bars, possibly empty.</returns>
    public async Task<Result<IReadOnlyList<CapitalBar>>> GetKLinesAsync(
        CapitalSymbol symbol,
        CapitalKLineType type,
        DateOnly from,
        DateOnly to,
        int decimals,
        CancellationToken cancellationToken = default)
    {
        if (!symbol.IsValid)
        {
            return CapitalErrors.InvalidArgument("商品代碼不合法。The symbol is invalid.");
        }

        if (!Enum.IsDefined(type))
        {
            return CapitalErrors.InvalidArgument("K 線類型只能是 Minute / Day / Week / Month。The bar type must be Minute, Day, Week or Month.");
        }

        if (decimals is < 0 or > CapitalPriceScale.MaxDecimals)
        {
            return CapitalErrors.InvalidArgument("decimals 必須在 0–9。decimals must be 0 to 9.");
        }

        if (from > to)
        {
            return CapitalErrors.InvalidArgument("起日不可晚於迄日。from must not be after to.");
        }

        if (type == CapitalKLineType.Minute && !CapitalExchangeClock.TryGetTimeZone(symbol.Exchange, out _))
        {
            return CapitalErrors.UnknownExchange(symbol.Exchange);
        }

        Result<JsonElement> response = await SendAsync(
                "kline",
                writer =>
                {
                    writer.WriteString("symbol", symbol.ToRequestString());
                    writer.WriteNumber("type", (int)type);
                    writer.WriteString("start", from.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                    writer.WriteString("end", to.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                },
                KLineTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.Error;
        }

        JsonElement result = response.GetValueOrThrow();

        if (!result.TryGetProperty("lines", out JsonElement linesElement) || linesElement.ValueKind != JsonValueKind.Array)
        {
            return CapitalErrors.Protocol("kline 的回應沒有 lines 陣列。The kline result has no lines array.");
        }

        List<string> lines = new(linesElement.GetArrayLength());

        foreach (JsonElement line in linesElement.EnumerateArray())
        {
            lines.Add(line.GetString() ?? string.Empty);
        }

        return CapitalKLineParser.Parse(symbol, type, lines, decimals);
    }

    /// <summary>
    /// 報價主機的連線狀態(<c>IsConnected</c> + <c>GetQuoteStatus</c>)。
    /// The quote server's connection status.
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>連線狀態。The status.</returns>
    public async Task<Result<CapitalConnectionStatus>> GetConnectionStatusAsync(CancellationToken cancellationToken = default)
    {
        Result<JsonElement> response = await SendAsync("status", null, ShortRequestTimeout, cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.Error;
        }

        JsonElement result = response.GetValueOrThrow();
        _ = CapitalWire.TryParseDomesticState(CapitalWire.GetString(result, "domestic"), out CapitalDomesticState domestic);
        _ = CapitalWire.TryParseOverseasOptionsState(CapitalWire.GetString(result, "overseasOptions"), out CapitalOverseasOptionsState options);

        return new CapitalConnectionStatus
        {
            OverseasOptions = options,
            OverseasOptionsReason = CapitalWire.GetString(result, "overseasOptionsReason"),
            SkcomVersion = CapitalWire.GetString(result, "skcomVersion"),
            Connected = result.TryGetProperty("connected", out JsonElement connected) && connected.ValueKind == JsonValueKind.True,
            Connections = CapitalWire.GetInt32(result, "connections") ?? 0,
            OutLimit = result.TryGetProperty("outLimit", out JsonElement outLimit) && outLimit.ValueKind == JsonValueKind.True,
            Domestic = domestic,
            DomesticReason = CapitalWire.GetString(result, "domesticReason"),
        };
    }

    private static CapitalHostState StateForFailure(Error error, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || error.Code == CapitalErrorCodes.RequestCancelled)
        {
            return CapitalHostState.Stopped;
        }

        return error.Code == CapitalErrorCodes.LoginFailed ? CapitalHostState.LoginFailed : CapitalHostState.Faulted;
    }

    private async Task<Result<JsonElement>> SendAsync(
        string method,
        Action<Utf8JsonWriter>? writeParams,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        CapitalHostProcess? host;
        CapitalHostState state;

        lock (_stateLock)
        {
            host = _host;
            state = _state;
        }

        if (host is null || state is not (CapitalHostState.Ready or CapitalHostState.Disconnected))
        {
            return CapitalErrors.HostNotReady(state);
        }

        return await host.RequestAsync(method, writeParams, timeout, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<IReadOnlyList<CapitalSymbol>>> SendSubscribeAsync(IReadOnlyList<CapitalSymbol> symbols, CancellationToken cancellationToken)
    {
        Result<JsonElement> response = await SendAsync("subscribe", writer => WriteSymbols(writer, symbols), ShortRequestTimeout, cancellationToken)
            .ConfigureAwait(false);

        return response.IsFailure ? response.Error : ReadSubscribed(response.GetValueOrThrow());
    }

    private static void WriteSymbols(Utf8JsonWriter writer, IReadOnlyList<CapitalSymbol> symbols)
    {
        writer.WriteStartArray("symbols");

        foreach (CapitalSymbol symbol in symbols)
        {
            writer.WriteStringValue(symbol.ToRequestString());
        }

        writer.WriteEndArray();
    }

    private static Result<IReadOnlyList<CapitalSymbol>> ReadSubscribed(JsonElement result)
    {
        if (!result.TryGetProperty("subscribed", out JsonElement subscribed) || subscribed.ValueKind != JsonValueKind.Array)
        {
            return CapitalErrors.Protocol("subscribe 的回應沒有 subscribed 陣列。The subscribe result has no subscribed array.");
        }

        List<CapitalSymbol> symbols = [];

        foreach (JsonElement element in subscribed.EnumerateArray())
        {
            if (CapitalSymbol.TryParse(element.GetString(), out CapitalSymbol symbol))
            {
                symbols.Add(symbol);
            }
        }

        return symbols;
    }

    private void SetState(CapitalHostState next)
    {
        CapitalHostState previous;

        lock (_stateLock)
        {
            previous = _state;

            if (previous == next)
            {
                return;
            }

            _state = next;
        }

        CapitalHostLog.StateChanged(_logger, previous, next);

        try
        {
            StateChanged?.Invoke(this, new CapitalHostStateChangedEventArgs(previous, next));
        }
#pragma warning disable CA1031 // 呼叫端處理常式的缺陷不能拖垮讀取迴圈或監督;記錄後繼續。A handler defect must not stop the reader or the supervisor.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            CapitalHostLog.StateHandlerFailed(_logger, exception);
        }
    }

    private void RegisterSecrets(CapitalCredentials credentials)
    {
        foreach (string value in new[] { credentials.LoginId, credentials.Password })
        {
            // 遮罩器不收短於 8 個字元的值;那種情況靠 Mask 裡的字面值替換。Short values rely on the literal replacement in Mask.
            if (value.Length >= SecretMasker.MinimumKnownSecretLength)
            {
                _ = _masker.RegisterKnownSecret(value);
            }
        }
    }

    /// <summary>
    /// 遮罩宿主送來的文字:先換掉帳密的字面值(不論長短,ID 忽略大小寫),再交給遮罩器。
    /// Masks text from the host: the credentials' literal values are replaced first, whatever their length, then the
    /// masker runs.
    /// </summary>
    private string Mask(string text)
    {
        string result = text;
        CapitalCredentials? credentials = _credentials;

        if (credentials is not null)
        {
            if (credentials.Password.Length > 0)
            {
                result = result.Replace(credentials.Password, "***", StringComparison.Ordinal);
            }

            if (credentials.LoginId.Length > 0)
            {
                result = result.Replace(credentials.LoginId, "<ID>", StringComparison.OrdinalIgnoreCase);
            }
        }

        // 0.3.0:帳務查詢拿到(或呼叫端傳入)的帳號也換掉。Account numbers seen by the account queries are replaced too.
        result = MaskAccounts(result);

        return _masker.MaskText(result) ?? string.Empty;
    }

    private void WarnOnce(string key, Action log)
    {
        if (_warnedOnce.TryAdd(key, 0))
        {
            log();
        }
    }
}
