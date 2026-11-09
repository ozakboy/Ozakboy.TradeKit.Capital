using System.Globalization;
using System.Runtime.InteropServices;
using Ozakboy.TradeKit.Capital.Host.Interop;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>
/// 國內報價探針(<c>probe-tw</c>):登入群益、連國內報價主機,對 <c>SKQuoteLib_RequestKLineAMByDate</c> 做一組固定的查詢,
/// 把原始回傳記下來,用來確認手冊沒寫清楚的部分:分 K 時間是開頭還是結束、13:30 收盤那根怎麼給、量的單位、能查多深、
/// 一次請求能給多長的區間、指數代碼、連續送請求會不會被擋、跟海外報價同時連線行不行。只讀行情,不碰下單。
/// 帳密錯誤一律不重試(錯太多次群益會鎖帳號)。
/// The domestic quote probe: logs in, connects to the domestic quote server, runs a fixed set of historical K-line
/// requests and records the raw replies. Market data only, never orders. A credential error is never retried.
/// </summary>
internal sealed class DomesticProbeRunner
{
    private const int KindConnected = 3001;
    private const int KindReady = 3003;
    private const int OverseasConnected = 3001;

    private static readonly TimeSpan QuietLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RequestLimit = TimeSpan.FromSeconds(180);

    // 國內期貨近月代碼候選:永豐是 TXFR1 / MXFR1 / TMFR1,群益的寫法手冊沒列,逐一試;期貨清單裡 TX / MTX / TM 開頭的也會補進來
    private static readonly string[] FuturesCandidates = ["TX00", "MTX00", "TMF00", "TM00", "TXF00", "MXF00", "TX0000", "MTX0000"];

    private readonly ProbeOptions _options;
    private readonly List<string> _futuresListCodes = [];
    private string[] _burstCodes = [];
    private readonly ProbeLog _log;
    private readonly object _gate = new();
    private readonly List<string> _currentLines = [];
    private readonly TaskCompletionSource<int> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _overseasConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _currentTest = "(none)";
    private int _completeCount;
    private DateTime _lastKLineUtc = DateTime.MinValue;
    private DateTime _lastStockListUtc = DateTime.MinValue;

    /// <summary>
    /// 建立探針。
    /// Creates the probe.
    /// </summary>
    /// <param name="options">選項(只用輸出目錄)。Options; only the output directory is used.</param>
    /// <param name="log">輸出。Output.</param>
    public DomesticProbeRunner(ProbeOptions options, ProbeLog log)
    {
        _options = options;
        _log = log;
    }

    /// <summary>
    /// 在 STA 執行緒上跑完整個探針流程。
    /// Runs the whole probe on the STA thread.
    /// </summary>
    /// <param name="userId">身分證字號(大寫)。The login ID (upper case).</param>
    /// <param name="password">密碼。The password.</param>
    /// <returns>結束碼:0 成功,其他見 Program。Exit code.</returns>
    public async Task<int> RunAsync(string userId, string password)
    {
        ISKCenterLib center;
        object replyLib;
        ISKQuoteLib quote;

        try
        {
            center = (ISKCenterLib)Create(SkComIds.CenterLibClsid);
            replyLib = Create(SkComIds.ReplyLibClsid);
            quote = (ISKQuoteLib)Create(SkComDomesticIds.QuoteLibClsid);
        }
        catch (Exception exception) when (exception is COMException or IOException or InvalidCastException)
        {
            _log.Event("create_failed", new Dictionary<string, object?> { ["hresult"] = $"0x{exception.HResult:X8}", ["message"] = exception.Message });
            ProbeLog.Say($"建立 SKCOM 物件失敗(0x{exception.HResult:X8})。請確認 SKCOM x64 已以系統管理員身分執行 install.bat 註冊。");
            return 3;
        }

        string apiVersion = Safe(() => center.SKCenterLib_GetSKAPIVersionAndBit(userId));
        _log.Event("api_version", new Dictionary<string, object?> { ["value"] = apiVersion });
        ProbeLog.Say($"SKCOM 版本:{apiVersion}");

        string skLogPath = Path.Combine(_options.OutputDirectory, "sklog");
        Directory.CreateDirectory(skLogPath);
        Report(center, "set_log_path", center.SKCenterLib_SetLogPath(skLogPath));

        ReplyEventsSink replySink = new();
        replySink.MessageReceived += message => _log.Event("announcement", new Dictionary<string, object?> { ["message"] = message });
        using ComEventConnection replyConnection = ComEventConnection.Advise(replyLib, SkComIds.ReplyLibEventsIid, replySink);

        QuoteEventsSink quoteSink = new();
        HookQuoteEvents(quoteSink);
        using ComEventConnection quoteConnection = ComEventConnection.Advise(quote, SkComDomesticIds.QuoteLibEventsIid, quoteSink);

        ProbeLog.Say("登入群益…");
        int loginCode = center.SKCenterLib_Login(userId, password);
        Report(center, "login", loginCode);

        if (loginCode != 0)
        {
            ProbeLog.Say($"登入失敗(代碼 {loginCode}):{Safe(() => center.SKCenterLib_GetReturnCodeMessage(loginCode))}。帳密類錯誤請勿連續重試,錯太多次會鎖帳號。");
            return 4;
        }

        ProbeLog.Say("登入成功,連線國內報價主機(等商品基本資料下載完成,最多 120 秒)…");
        int enter = quote.SKQuoteLib_EnterMonitorLONG();
        Report(center, "enter_monitor", enter);

        if (enter != 0)
        {
            ProbeLog.Say($"國內報價連線被拒(代碼 {enter}):{Safe(() => center.SKCenterLib_GetReturnCodeMessage(enter))}。常見原因:沒有證券帳戶,或還沒簽署證券 API 下單聲明書。");
            return 5;
        }

        Task finished = await Task.WhenAny(_ready.Task, Task.Delay(TimeSpan.FromSeconds(120))).ConfigureAwait(true);

        if (finished != _ready.Task)
        {
            ProbeLog.Say("120 秒內沒收到 3003(商品基本資料下載完成),照樣繼續查詢,看看結果。");
        }

        Report(center, "is_connected", quote.SKQuoteLib_IsConnected());
        Report(center, "request_server_time", quote.SKQuoteLib_RequestServerTime());

        await DumpStockListsAsync(center, quote).ConfigureAwait(true);
        await RunKLineSuiteAsync(center, quote).ConfigureAwait(true);
        await RunBurstAsync(center, quote).ConfigureAwait(true);

        int status = quote.SKQuoteLib_GetQuoteStatus(out int connections, out bool outLimit);
        Report(center, "quote_status", status, ("connections", connections), ("out_limit", outLimit));

        await RunCoexistenceAsync(center, quote).ConfigureAwait(true);

        Report(center, "leave_monitor", quote.SKQuoteLib_LeaveMonitor());
        _log.Event("done", new Dictionary<string, object?>());
        ProbeExitGuard.Arm(0);
        ProbeLog.Say($"完成。輸出在 {_options.OutputDirectory}(sklog 子目錄是群益自己的紀錄,含帳號,不用給別人)");
        return 0;
    }

    private static object Create(Guid clsid)
    {
        Type type = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;
        return Activator.CreateInstance(type)!;
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (COMException exception)
        {
            return $"(COMException 0x{exception.HResult:X8})";
        }
    }

    private static string Day(DateTime value) => value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static async Task WaitQuietAsync(Func<DateTime> lastEventUtc, TimeSpan quiet, TimeSpan max)
    {
        DateTime started = DateTime.UtcNow;

        while (DateTime.UtcNow - started < max)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(true);
            DateTime last = lastEventUtc();
            DateTime reference = last == DateTime.MinValue || last < started ? started : last;

            if (DateTime.UtcNow - reference > quiet)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 上市(0)、上櫃(1)、期貨(2)的商品清單。第一輪實測(2026-11-07,只有期貨戶):0 / 1 只回 <c>##,,;</c>,
    /// 證券的 K 線全部 9999(群益手冊:沒有證券戶拿不到上市櫃資料);所以第二輪改測國內期貨,期貨清單的代碼收進候選。
    /// </summary>
    private async Task DumpStockListsAsync(ISKCenterLib center, ISKQuoteLib quote)
    {
        foreach (short market in new short[] { 0, 1, 2 })
        {
            ProbeLog.Say($"下載市場 {market} 的商品清單…");
            Report(center, "request_stock_list", quote.SKQuoteLib_RequestStockList(market), ("market", market));
            await WaitQuietAsync(() => _lastStockListUtc, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60)).ConfigureAwait(true);
        }

        lock (_gate)
        {
            _log.Event("futures_list_candidates", new Dictionary<string, object?> { ["values"] = _futuresListCodes.ToArray() });
        }
    }

    /// <summary>
    /// K 線查詢組(國內期貨為主):先對每個候選代碼查最近 7 天全盤 1 分 K,找出台指 / 小台 / 微台近月在群益的代碼,
    /// 再對台指做深度、一次查長區間、AM 盤 vs 全盤、日 K 的測試。每個查詢等到 OnKLineComplete 或 15 秒沒新資料才送下一個。
    /// </summary>
    private async Task RunKLineSuiteAsync(ISKCenterLib center, ISKQuoteLib quote)
    {
        DateTime today = DateTime.Today;

        if (!_options.Symbols.Contains("full", StringComparer.Ordinal))
        {
            await RunThirdRoundAsync(center, quote).ConfigureAwait(true);
            return;
        }

        // 證券留一筆當紀錄(只有期貨戶時預期 9999)
        await RunOneAsync(center, quote, "stock_m1_recent", "2330", 0, today.AddDays(-7), today).ConfigureAwait(true);

        List<string> candidates = [.. FuturesCandidates];

        lock (_gate)
        {
            candidates.AddRange(_futuresListCodes.Where(code => !candidates.Contains(code, StringComparer.Ordinal)).Take(12));
        }

        Dictionary<string, int> found = new(StringComparer.Ordinal);

        foreach (string code in candidates)
        {
            int lines = await RunOneAsync(center, quote, $"fut_m1_recent_{code}", code, 0, today.AddDays(-7), today).ConfigureAwait(true);
            found[code] = lines;
        }

        string? tx = FirstWithData(found, ["TX00", "TXF00", "TX0000"], "TX");
        string? mtx = FirstWithData(found, ["MTX00", "MXF00", "MTX0000"], "MTX");
        string? tmf = FirstWithData(found, ["TMF00", "TM00", "TMF0000"], "TM");
        _log.Event("futures_codes_chosen", new Dictionary<string, object?> { ["tx"] = tx, ["mtx"] = mtx, ["tmf"] = tmf, ["lines"] = found });

        if (tx is null)
        {
            ProbeLog.Say("候選代碼都查不到期貨 K 線,只記錄清單。");
            return;
        }

        List<(string Name, string Code, short Type, short Session, DateTime From, DateTime To)> tests =
        [
            ("tx_m1_am_recent", tx, 0, 1, today.AddDays(-7), today),
            ("tx_m1_today", tx, 0, 0, today, today),
            ("tx_m1_6months", tx, 0, 0, today.AddDays(-183), today),
            ("tx_m1_1y_ago", tx, 0, 0, today.AddYears(-1).AddDays(-7), today.AddYears(-1)),
            ("tx_m1_3y_ago", tx, 0, 0, today.AddYears(-3).AddDays(-7), today.AddYears(-3)),
            ("tx_m1_2020", tx, 0, 0, new DateTime(2020, 3, 2), new DateTime(2020, 3, 6)),
            ("tx_m1_2018", tx, 0, 0, new DateTime(2018, 1, 2), new DateTime(2018, 1, 5)),
            ("tx_d_5y_full", tx, 4, 0, today.AddYears(-5), today),
            ("tx_d_5y_am", tx, 4, 1, today.AddYears(-5), today),
            ("tx_d_2015", tx, 4, 0, new DateTime(2015, 1, 1), new DateTime(2015, 1, 31)),

            // 結算日換月:2026-10-21(週三)是 10 月合約最後交易日,看前後連續月怎麼接
            ("tx_m1_rollover", tx, 0, 0, new DateTime(2026, 10, 20), new DateTime(2026, 10, 22)),
        ];

        if (mtx is not null)
        {
            tests.Add(("mtx_d_1m", mtx, 4, 0, today.AddMonths(-1), today));
        }

        if (tmf is not null)
        {
            tests.Add(("tmf_m1_6months", tmf, 0, 0, today.AddDays(-183), today));
        }

        foreach ((string name, string code, short type, short session, DateTime from, DateTime to) in tests)
        {
            await RunOneAsync(center, quote, name, code, type, from, to, session).ConfigureAwait(true);
        }

        _burstCodes = [.. new[] { tx, mtx, tmf }.OfType<string>()];
    }

    /// <summary>
    /// 第三輪(預設;第二輪整組用 <c>--symbol full</c> 重跑):第二輪已確認 TX00 / MTX00 可用、分 K 時間是結束時刻、
    /// 一次最多約 60,000 筆、1 年前查不到;這輪查 ① 週五夜盤量是 2 倍是不是查詢起日落在週末造成的 ② 1 分 K 最早到哪個月
    /// ③ 60,000 筆上限與截掉哪一頭 ④ 微台代碼 TM0000 ⑤ AM 盤分 K 的 13:45 收盤價 ⑥ 國內查完再查海外 K 線行不行。
    /// </summary>
    private async Task RunThirdRoundAsync(ISKCenterLib center, ISKQuoteLib quote)
    {
        DateTime today = DateTime.Today;
        List<(string Name, string Code, short Type, short Session, DateTime From, DateTime To)> tests =
        [
            ("vol_1102_only", "TX00", 0, 0, new DateTime(2026, 11, 2), new DateTime(2026, 11, 2)),
            ("vol_1031_1102", "TX00", 0, 0, new DateTime(2026, 10, 31), new DateTime(2026, 11, 2)),
            ("vol_1030_1102", "TX00", 0, 0, new DateTime(2026, 10, 30), new DateTime(2026, 11, 2)),
            ("vol_1101_1102", "TX00", 0, 0, new DateTime(2026, 11, 1), new DateTime(2026, 11, 2)),
            ("vol_1026_1030", "TX00", 0, 0, new DateTime(2026, 10, 26), new DateTime(2026, 10, 30)),
            ("vol_1024_1027", "TX00", 0, 0, new DateTime(2026, 10, 24), new DateTime(2026, 10, 27)),
            ("depth_202511", "TX00", 0, 0, new DateTime(2025, 11, 1), new DateTime(2025, 11, 30)),
            ("depth_202604", "TX00", 0, 0, new DateTime(2026, 4, 1), new DateTime(2026, 4, 30)),
            ("depth_202605", "TX00", 0, 0, new DateTime(2026, 5, 1), new DateTime(2026, 5, 31)),
            ("depth_202606", "TX00", 0, 0, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30)),
            ("depth_202607", "TX00", 0, 0, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31)),
            ("depth_202608", "TX00", 0, 0, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31)),
            ("cap_0801_1107", "TX00", 0, 0, new DateTime(2026, 8, 1), today),
            ("am_m1_1102_1106", "TX00", 0, 1, new DateTime(2026, 11, 2), new DateTime(2026, 11, 6)),
            ("tm_m1_recent", "TM0000", 0, 0, today.AddDays(-7), today),
            ("tm_d_1m", "TM0000", 4, 0, today.AddMonths(-1), today),
            ("mtx_m1_depth_202606", "MTX00", 0, 0, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30)),
        ];

        foreach ((string name, string code, short type, short session, DateTime from, DateTime to) in tests)
        {
            await RunOneAsync(center, quote, name, code, type, from, to, session).ConfigureAwait(true);
        }

        _burstCodes = ["TX00", "MTX00"];
    }

    private static string? FirstWithData(Dictionary<string, int> found, string[] preferred, string prefix)
    {
        foreach (string code in preferred)
        {
            if (found.TryGetValue(code, out int lines) && lines > 0)
            {
                return code;
            }
        }

        return found.Where(pair => pair.Value > 0 && pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(pair => pair.Key).FirstOrDefault();
    }

    private async Task<int> RunOneAsync(ISKCenterLib center, ISKQuoteLib quote, string name, string code, short type, DateTime from, DateTime to, short session = 0)
    {
        lock (_gate)
        {
            _currentTest = name;
            _currentLines.Clear();
        }

        int completesBefore = Volatile.Read(ref _completeCount);
        DateTime started = DateTime.UtcNow;
        ProbeLog.Say($"K 線 {name}:{code} 類型 {type} 盤別 {session} {Day(from)}–{Day(to)}");
        int result = quote.SKQuoteLib_RequestKLineAMByDate(code, type, 1, session, Day(from), Day(to), 1);
        bool completed = false;

        if (result == 0)
        {
            while (DateTime.UtcNow - started < RequestLimit)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(true);

                if (Volatile.Read(ref _completeCount) > completesBefore)
                {
                    completed = true;
                    break;
                }

                DateTime last = _lastKLineUtc < started ? started : _lastKLineUtc;

                if (DateTime.UtcNow - last > QuietLimit)
                {
                    break;
                }
            }
        }

        List<string> lines;

        lock (_gate)
        {
            lines = [.. _currentLines];
            _currentTest = "(between)";
        }

        Report(
            center,
            "kline_" + name,
            result,
            ("code_no", code),
            ("type", type),
            ("session", session),
            ("from", Day(from)),
            ("to", Day(to)),
            ("lines", lines.Count),
            ("completed_event", completed),
            ("elapsed_ms", (long)(DateTime.UtcNow - started).TotalMilliseconds),
            ("first", lines.Take(3).ToArray()),
            ("last", lines.TakeLast(3).ToArray()));
        ProbeLog.Say($"  → 回傳 {result},{lines.Count} 筆,{(completed ? "收到結束事件" : "沒收到結束事件")}");
        return lines.Count;
    }

    /// <summary>連續送請求不等回應(找到的期貨代碼,各送兩次):看會不會被擋、資料會不會混在一起(OnNotifyKLineData 帶代碼可分開)。</summary>
    private async Task RunBurstAsync(ISKCenterLib center, ISKQuoteLib quote)
    {
        DateTime today = DateTime.Today;
        string[] burst = [.. _burstCodes, .. _burstCodes];

        if (burst.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _currentTest = "burst";
            _currentLines.Clear();
        }

        int completesBefore = Volatile.Read(ref _completeCount);
        DateTime started = DateTime.UtcNow;
        ProbeLog.Say($"連續送 {burst.Length} 個 K 線請求(不等回應)…");

        foreach (string code in burst)
        {
            int result = quote.SKQuoteLib_RequestKLineAMByDate(code, 0, 1, 0, Day(today.AddDays(-5)), Day(today), 1);
            Report(center, "burst_request", result, ("code_no", code));
        }

        while (DateTime.UtcNow - started < RequestLimit && Volatile.Read(ref _completeCount) - completesBefore < burst.Length)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(true);
            DateTime last = _lastKLineUtc < started ? started : _lastKLineUtc;

            if (DateTime.UtcNow - last > QuietLimit)
            {
                break;
            }
        }

        List<string> lines;

        lock (_gate)
        {
            lines = [.. _currentLines];
            _currentTest = "(between)";
        }

        Dictionary<string, int> perCode = lines
            .GroupBy(line => line.Split('\t')[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        _log.Event("burst_done", new Dictionary<string, object?>
        {
            ["completes"] = Volatile.Read(ref _completeCount) - completesBefore,
            ["lines"] = lines.Count,
            ["per_code"] = perCode,
            ["elapsed_ms"] = (long)(DateTime.UtcNow - started).TotalMilliseconds,
        });
    }

    /// <summary>同一個行程再連海外報價主機,確認國內查詢照常:宿主之後要兩邊一起掛。</summary>
    private async Task RunCoexistenceAsync(ISKCenterLib center, ISKQuoteLib quote)
    {
        ProbeLog.Say("同時連海外報價主機,確認國內查詢還能用…");
        ISKOSQuoteLib overseas;

        try
        {
            overseas = (ISKOSQuoteLib)Create(SkComIds.OsQuoteLibClsid);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            _log.Event("overseas_create_failed", new Dictionary<string, object?> { ["hresult"] = $"0x{exception.HResult:X8}" });
            return;
        }

        OsQuoteEventsSink overseasSink = new();
        overseasSink.Connected += (code, socketCode) =>
        {
            _log.Event("overseas_on_connect", new Dictionary<string, object?> { ["code"] = code, ["socket_code"] = socketCode });

            if (code == OverseasConnected)
            {
                _overseasConnected.TrySetResult(code);
            }
        };

        using ComEventConnection overseasConnection = ComEventConnection.Advise(overseas, SkComIds.OsQuoteLibEventsIid, overseasSink);
        Report(center, "overseas_enter_monitor", overseas.SKOSQuoteLib_EnterMonitorLONG());
        Task finished = await Task.WhenAny(_overseasConnected.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(true);
        _log.Event("overseas_connected", new Dictionary<string, object?> { ["connected"] = finished == _overseasConnected.Task });

        DateTime today = DateTime.Today;
        string coexistCode = _burstCodes.FirstOrDefault() ?? "TX00";
        await RunOneAsync(center, quote, "coexist_m1", coexistCode, 0, today.AddDays(-3), today).ConfigureAwait(true);
        int overseasLines = 0;
        overseasSink.KLine += (_, data) =>
        {
            overseasLines++;

            if (overseasLines <= 3)
            {
                _log.Raw("overseas-kline-after", data);
            }
        };

        // 第二輪傳 "SUSDTWD" 回 1042;宿主實際送的是「交易所,代碼」
        Report(center, "overseas_kline_after", overseas.SKOSQuoteLib_RequestKLineByDate("FX,SUSDTWD", 0, Day(today.AddDays(-3)), Day(today), 1));
        await Task.Delay(TimeSpan.FromSeconds(8)).ConfigureAwait(true);
        _log.Event("overseas_kline_after_lines", new Dictionary<string, object?> { ["lines"] = overseasLines });
        Report(center, "domestic_is_connected_after", quote.SKQuoteLib_IsConnected());
        Report(center, "overseas_leave_monitor", overseas.SKOSQuoteLib_LeaveMonitor());
    }

    private void Report(ISKCenterLib center, string step, int code, params (string Key, object? Value)[] extra)
    {
        Dictionary<string, object?> fields = new(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["message"] = Safe(() => center.SKCenterLib_GetReturnCodeMessage(code)),
        };

        foreach ((string key, object? value) in extra)
        {
            fields[key] = value;
        }

        _log.Event(step, fields);
    }

    private void HookQuoteEvents(QuoteEventsSink sink)
    {
        sink.Connection += (kind, code) =>
        {
            _log.Event("on_connection", new Dictionary<string, object?> { ["conn_kind"] = kind, ["code"] = code });

            if (kind == KindReady)
            {
                _ready.TrySetResult(kind);
            }
            else if (kind == KindConnected)
            {
                ProbeLog.Say("已連線(3001),等商品基本資料…");
            }
        };

        sink.KLine += (stockNo, data) =>
        {
            _lastKLineUtc = DateTime.UtcNow;
            string test;

            lock (_gate)
            {
                test = _currentTest;
                _currentLines.Add(stockNo + "\t" + data);
            }

            _log.Raw("kline-" + test, stockNo + "\t" + data);
        };

        sink.KLineComplete += end =>
        {
            Interlocked.Increment(ref _completeCount);
            string test;

            lock (_gate)
            {
                test = _currentTest;
            }

            _log.Event("kline_complete", new Dictionary<string, object?> { ["test"] = test, ["end"] = end });
        };

        sink.ServerTime += (hour, minute, second, total) =>
            _log.Event("server_time", new Dictionary<string, object?> { ["hour"] = hour, ["minute"] = minute, ["second"] = second, ["total"] = total });

        sink.StockList += (market, data) =>
        {
            _lastStockListUtc = DateTime.UtcNow;
            _log.Raw("stocklist-" + market.ToString(CultureInfo.InvariantCulture), data);

            if (market == 2)
            {
                // 格式待實測:先假設「代碼,名稱,…;代碼,名稱,…」,取每段第一欄裡 TX / MTX / TM 開頭的
                IEnumerable<string> codes = data.Split(';')
                    .Select(item => item.Split(',')[0].Trim())
                    .Where(code => code.StartsWith("TX", StringComparison.Ordinal) || code.StartsWith("MTX", StringComparison.Ordinal) || code.StartsWith("TM", StringComparison.Ordinal));

                lock (_gate)
                {
                    foreach (string code in codes.Where(code => !_futuresListCodes.Contains(code, StringComparer.Ordinal)))
                    {
                        _futuresListCodes.Add(code);
                    }
                }
            }
        };
    }
}
