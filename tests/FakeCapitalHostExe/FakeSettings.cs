using System.Globalization;

namespace FakeCapitalHostExe;

/// <summary>
/// 假宿主的行為設定,全部來自環境變數(前綴 <c>FAKE_CAPITAL_</c>);沒設的照實測行為(DESIGN §5)。
/// The fake host's behaviour, all from FAKE_CAPITAL_* environment variables; unset means the measured behaviour.
/// </summary>
internal sealed class FakeSettings
{
    private readonly Lock _gate = new();

    /// <summary>Login 回傳的代碼(300 密碼錯、307 鎖定…);0 = 成功。The Login return code.</summary>
    public int LoginCode { get; private init; }

    /// <summary>設了就比對:帳號不符回 306、密碼不符回 300(驗證帳密真的經環境變數送到)。Expected ID; 306 on mismatch.</summary>
    public string? ExpectedId { get; private init; }

    /// <summary>同上,密碼。Expected password; 300 on mismatch.</summary>
    public string? ExpectedPassword { get; private init; }

    /// <summary>Open 失敗的 HRESULT(0x80040154 = 沒註冊)。The HRESULT Open fails with.</summary>
    public int? OpenHResult { get; private init; }

    /// <summary>EnterMonitor 後永遠不送 3001。Never sends 3001.</summary>
    public bool NoConnect { get; private init; }

    /// <summary>ready 後多久送 3002(每個行程只斷一次;重新 EnterMonitor 後送 3001)。When to send 3002 after ready.</summary>
    public TimeSpan? DisconnectAfter { get; private init; }

    /// <summary>斷線後前幾次重新 EnterMonitor 不回 3001(驗證重連退避)。Reconnect attempts that get no 3001.</summary>
    public int ReconnectFailures { get; private init; }

    /// <summary>第一次 RequestStocks 之後多久崩潰(結束碼 1,不輸出任何東西)。When to crash after the first RequestStocks.</summary>
    public TimeSpan? CrashAfter { get; private init; }

    /// <summary>設了就只崩潰一次:檔案不存在才崩潰並建立它。A marker file so the crash happens only once.</summary>
    public string? CrashOnceFile { get; private init; }

    /// <summary>每檔訂閱當下回補幾筆歷史成交。History ticks replayed per symbol.</summary>
    public int HistoryTicks { get; private init; } = 3;

    /// <summary>即時成交的間隔(0 = 不送)。The live tick interval; zero sends none.</summary>
    public TimeSpan LiveTickInterval { get; private init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>RequestStocks 的字串含這個代碼就回 1012(假宿主自訂的拒絕代碼)。A code whose RequestStocks gets 1012.</summary>
    public string? RejectSymbol { get; private init; }

    /// <summary>登入前送一則含登入帳號的公告(驗證遮罩)。Sends an announcement containing the login ID.</summary>
    public bool AnnounceId { get; private init; }

    /// <summary>呼叫紀錄檔(每行「pid 動作 內容」)。The call log file.</summary>
    public string? CallLog { get; private init; }

    public TimeSpan ConnectTimeout { get; private init; } = TimeSpan.FromSeconds(60);

    public TimeSpan ProductsTimeout { get; private init; } = TimeSpan.FromSeconds(60);

    public TimeSpan KLineQuiet { get; private init; } = TimeSpan.FromSeconds(1.5);

    public TimeSpan KLineNoData { get; private init; } = TimeSpan.FromSeconds(10);

    public IReadOnlyList<TimeSpan> ReconnectDelays { get; private init; } =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    // ── 國內報價(前綴 FAKE_CAPITAL_TW_)。Domestic quotes, prefix FAKE_CAPITAL_TW_. ──

    /// <summary>OpenDomestic 失敗的 HRESULT。The HRESULT OpenDomestic fails with.</summary>
    public int? TwOpenHResult { get; private init; }

    /// <summary>國內 EnterMonitorLONG 的回傳代碼(非 0 = 國內不可用)。The domestic EnterMonitorLONG code.</summary>
    public int TwEnterCode { get; private init; }

    /// <summary>國內只送 3001、永遠不送 3003(驗證 3003 逾時 → 不可用)。Sends 3001 but never 3003.</summary>
    public bool TwNoReady { get; private init; }

    /// <summary>3001 之後多久送 3003(實測約 0.5 秒)。The delay from 3001 to 3003; about 0.5 s as measured.</summary>
    public TimeSpan TwReadyDelay { get; private init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>國內就緒後多久送 3002(每個行程只斷一次;重新 EnterMonitor 後再送 3001、3003)。When to send the domestic 3002.</summary>
    public TimeSpan? TwDisconnectAfter { get; private init; }

    /// <summary>一個請求最多幾筆(實測 60,000,超過保留最新、截掉最舊)。The per-request row cap; 60,000 as measured.</summary>
    public int TwKLineLimit { get; private init; } = 60_000;

    /// <summary>國內 K 線不送 OnKLineComplete(驗證逾時)。Never sends OnKLineComplete.</summary>
    public bool TwNoComplete { get; private init; }

    /// <summary>父行程看門狗的檢查間隔(沒設 = 宿主預設 5 秒)。The parent watchdog interval; unset = the host default.</summary>
    public TimeSpan? ParentCheckInterval { get; private init; }

    /// <summary>結束保險的寬限(沒設 = 宿主預設 15 秒)。The exit guard's grace; unset = the host default.</summary>
    public TimeSpan? ExitGrace { get; private init; }

    public TimeSpan DomesticReadyTimeout { get; private init; } = TimeSpan.FromSeconds(60);

    public TimeSpan DomesticKLineTimeout { get; private init; } = TimeSpan.FromSeconds(60);

    public static FakeSettings FromEnvironment() => new()
    {
        ParentCheckInterval = Millis("PARENT_CHECK_MS"),
        ExitGrace = Millis("EXIT_GRACE_MS"),
        TwOpenHResult = HResult("TW_OPEN_HRESULT"),
        TwEnterCode = Int("TW_ENTER_CODE") ?? 0,
        TwNoReady = Text("TW_NO_READY") == "1",
        TwReadyDelay = Millis("TW_READY_DELAY_MS") ?? TimeSpan.FromMilliseconds(500),
        TwDisconnectAfter = Millis("TW_DISCONNECT_AFTER_MS"),
        TwKLineLimit = Int("TW_KLINE_LIMIT") ?? 60_000,
        TwNoComplete = Text("TW_NO_COMPLETE") == "1",
        DomesticReadyTimeout = Millis("TW_READY_TIMEOUT_MS") ?? TimeSpan.FromSeconds(60),
        DomesticKLineTimeout = Millis("TW_KLINE_TIMEOUT_MS") ?? TimeSpan.FromSeconds(60),
        LoginCode = Int("LOGIN_CODE") ?? 0,
        ExpectedId = Text("EXPECT_ID"),
        ExpectedPassword = Text("EXPECT_PASSWORD"),
        OpenHResult = HResult("OPEN_HRESULT"),
        NoConnect = Text("NO_CONNECT") == "1",
        DisconnectAfter = Millis("DISCONNECT_AFTER_MS"),
        ReconnectFailures = Int("RECONNECT_FAILURES") ?? 0,
        CrashAfter = Millis("CRASH_AFTER_MS"),
        CrashOnceFile = Text("CRASH_ONCE_FILE"),
        HistoryTicks = Int("HISTORY_TICKS") ?? 3,
        LiveTickInterval = Millis("LIVE_TICK_MS") ?? TimeSpan.FromMilliseconds(200),
        RejectSymbol = Text("REJECT_SYMBOL"),
        AnnounceId = Text("ANNOUNCE_ID") == "1",
        CallLog = Text("CALL_LOG"),
        ConnectTimeout = Millis("CONNECT_TIMEOUT_MS") ?? TimeSpan.FromSeconds(60),
        ProductsTimeout = Millis("PRODUCTS_TIMEOUT_MS") ?? TimeSpan.FromSeconds(60),
        KLineQuiet = Millis("KLINE_QUIET_MS") ?? TimeSpan.FromSeconds(1.5),
        KLineNoData = Millis("KLINE_NODATA_MS") ?? TimeSpan.FromSeconds(10),
        ReconnectDelays = MillisList("RECONNECT_DELAYS_MS") ??
            [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)],
    };

    /// <summary>附加一行呼叫紀錄(多個行程可能同時寫,失敗就重試幾次)。Appends one line to the call log.</summary>
    public void Record(string line)
    {
        if (CallLog is null)
        {
            return;
        }

        string text = string.Create(CultureInfo.InvariantCulture, $"{Environment.ProcessId} {line}{Environment.NewLine}");

        lock (_gate)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.AppendAllText(CallLog, text);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(10);
                }
            }
        }
    }

    private static string? Text(string name)
    {
        string? value = Environment.GetEnvironmentVariable("FAKE_CAPITAL_" + name);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static int? Int(string name) =>
        int.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static TimeSpan? Millis(string name) => Int(name) is { } value ? TimeSpan.FromMilliseconds(value) : null;

    private static TimeSpan[]? MillisList(string name) =>
        Text(name)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => TimeSpan.FromMilliseconds(int.Parse(part, CultureInfo.InvariantCulture)))
            .ToArray();

    private static int? HResult(string name)
    {
        string? text = Text(name);

        if (text is null)
        {
            return null;
        }

        string digits = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return unchecked((int)uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}
