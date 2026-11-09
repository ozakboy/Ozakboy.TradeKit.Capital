using System.Collections.Concurrent;

namespace Ozakboy.TradeKit.Capital.Host.Tests.Support;

/// <summary>
/// 可程式化的假 SKCOM:記下每個呼叫、回傳設定好的代碼,事件由測試觸發或依設定自動送出。
/// 預設行為比照實測(DESIGN §5):EnterMonitorLONG 後非同步送 3001(不會有 3003)、商品清單以 <c>##</c> 那筆結尾。
/// A scriptable fake SKCOM that records every call and returns configured codes; events are raised by the test or
/// automatically. Defaults follow the measured behaviour: 3001 (never 3003) arrives asynchronously after
/// EnterMonitorLONG, and the product list ends with a "##" row.
/// </summary>
internal sealed partial class ScriptedSkQuoteSource : ISkQuoteSource
{
    private readonly ConcurrentQueue<string> _calls = new();
    private readonly ConcurrentDictionary<int, int> _indexLookups = new();

    public event Action<string>? Announcement;

    public event Action<int>? Connection;

    public event Action<string>? Product;

    public event Action<string>? ProductDetail;

    public event Action<string, string>? KLine;

    public event Action<int>? Quote;

    public event Action<SkTickData>? Tick;

    public SkOpenFailure? OpenFailure { get; set; }

    public int LoginCode { get; set; }

    /// <summary>登入時送出的公告(群益的公告可能含登入帳號)。An announcement raised during Login.</summary>
    public string? LoginAnnouncement { get; set; }

    /// <summary>GetLastLogInfo 的內容(群益的紀錄可能含登入帳號)。What GetLastLogInfo returns.</summary>
    public string LastLogInfo { get; set; } = "last log";

    public int EnterCode { get; set; }

    /// <summary>EnterMonitor 後是否自動送 3001(實測只會有 3001)。Whether 3001 follows EnterMonitor automatically.</summary>
    public bool AutoConnect { get; set; } = true;

    public int StocksCode { get; set; }

    public int TicksCode { get; set; }

    public int KLineCode { get; set; }

    /// <summary>RequestOverseaProducts 之後自動送的列(null = 不送,用來測逾時)。Rows sent after RequestOverseaProducts.</summary>
    public string[]? ProductRows { get; set; } =
    [
        "FX,國際外匯,SUSDTWD,新台幣,99991231,0",
        "CME,芝加哥商品交易所,NQ0000,小那熱2612,20261218,20261218",
        "##,,,,",
    ];

    /// <summary>GetOverseaProductDetail 之後自動送的列。Rows sent after GetOverseaProductDetail.</summary>
    public string[]? DetailRows { get; set; } =
    [
        "CME,芝加哥商品交易所,NQ0000,小那熱2612,CME,NQ_202612,20261218,20261218,5",
        "FX,國際外匯,SUSDTWD,新台幣,--,--,99991231,0,2",
        "##,,,,,,",
    ];

    /// <summary>RequestKLineByDate 之後自動送的 (代碼, 資料)。K-line rows sent after RequestKLineByDate.</summary>
    public List<(string Code, string Data)> KLineRows { get; } = [];

    /// <summary>索引 → 報價(GetStockByIndexNineDigitLONG)。Index to quote.</summary>
    public ConcurrentDictionary<int, SkQuoteData> Quotes { get; } = new();

    public string[] Calls => [.. _calls];

    public int CallCount(string prefix) => _calls.Count(call => call.StartsWith(prefix, StringComparison.Ordinal));

    public int IndexLookups(int index) => _indexLookups.TryGetValue(index, out int count) ? count : 0;

    public void RaiseConnection(int code) => Connection?.Invoke(code);

    public void RaiseQuote(int index) => Quote?.Invoke(index);

    public void RaiseTick(SkTickData tick) => Tick?.Invoke(tick);

    public void RaiseAnnouncement(string message) => Announcement?.Invoke(message);

    public void RaiseKLine(string code, string data) => KLine?.Invoke(code, data);

    public SkOpenFailure? Open()
    {
        _calls.Enqueue("open");
        return OpenFailure;
    }

    public int SetLogPath(string path)
    {
        _calls.Enqueue("set_log_path " + path);
        return 0;
    }

    public int Login(string loginId, string password)
    {
        _calls.Enqueue("login");

        if (LoginAnnouncement is { } announcement)
        {
            Announcement?.Invoke(announcement);
        }

        return LoginCode;
    }

    public string GetReturnCodeMessage(int code) => "MSG_" + code.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string GetLastLogInfo() => LastLogInfo;

    public int EnterMonitor()
    {
        _calls.Enqueue("enter");

        if (EnterCode == 0 && AutoConnect)
        {
            Later(() => Connection?.Invoke(3001));
        }

        return EnterCode;
    }

    public int LeaveMonitor()
    {
        _calls.Enqueue("leave");
        return 0;
    }

    public int IsConnected() => 1;

    public int GetQuoteStatus(out int connections, out bool outLimit)
    {
        connections = 0;
        outLimit = false;
        return 0;
    }

    public int RequestOverseaProducts()
    {
        _calls.Enqueue("products");

        if (ProductRows is { } rows)
        {
            Later(() =>
            {
                foreach (string row in rows)
                {
                    Product?.Invoke(row);
                }
            });
        }

        return 0;
    }

    public int GetOverseaProductDetail(short type)
    {
        _calls.Enqueue("detail " + type.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (DetailRows is { } rows)
        {
            Later(() =>
            {
                foreach (string row in rows)
                {
                    ProductDetail?.Invoke(row);
                }
            });
        }

        return 0;
    }

    public int RequestStocks(string stockNos)
    {
        _calls.Enqueue("stocks " + stockNos);
        return StocksCode;
    }

    public int RequestTicks(string stockNo)
    {
        _calls.Enqueue("ticks " + stockNo);
        return TicksCode;
    }

    public int RequestKLineByDate(string stockNo, short type, string startDate, string endDate, short minuteNumber)
    {
        _calls.Enqueue($"kline {stockNo} {type} {startDate} {endDate} {minuteNumber}");

        if (KLineCode == 0)
        {
            (string Code, string Data)[] rows = [.. KLineRows];
            Later(() =>
            {
                foreach ((string code, string data) in rows)
                {
                    KLine?.Invoke(code, data);
                }
            });
        }

        return KLineCode;
    }

    public int GetStockByIndex(int index, out SkQuoteData quote)
    {
        _ = _indexLookups.AddOrUpdate(index, 1, (_, count) => count + 1);
        return Quotes.TryGetValue(index, out quote) ? 0 : 1;
    }

    // ── 國內報價。預設照 2026-11-07 probe-tw 實測:EnterMonitorLONG 回 0 → 3001 → 3003;每個 K 線請求結束都有 OnKLineComplete("##")。
    // Domestic quotes. Defaults follow the 2026-11-07 measurements: 3001 then 3003 after EnterMonitorLONG, and one
    // OnKLineComplete("##") at the end of every K-line request. Domestic calls are recorded apart from the overseas ones.

    public event Action<int>? DomesticConnection;

    public event Action<string, string>? DomesticKLine;

    public event Action<string>? DomesticKLineComplete;

    private readonly ConcurrentQueue<string> _domesticCalls = new();

    public SkOpenFailure? DomesticOpenFailure { get; set; }

    /// <summary>OpenDomestic 直接擲例外(例如掛事件失敗)。OpenDomestic throws.</summary>
    public bool DomesticOpenThrows { get; set; }

    public int DomesticEnterCode { get; set; }

    /// <summary>國內 EnterMonitor 後自動送 3001、3003。Whether 3001 and 3003 follow automatically.</summary>
    public bool DomesticAutoReady { get; set; } = true;

    public int DomesticKLineCode { get; set; }

    /// <summary>國內 K 線請求後自動送結束事件。Whether OnKLineComplete follows automatically.</summary>
    public bool DomesticAutoComplete { get; set; } = true;

    /// <summary>國內 K 線請求後自動送的 (代碼, 資料)。Rows sent after a domestic K-line request.</summary>
    public List<(string Code, string Data)> DomesticKLineRows { get; } = [];

    public string[] DomesticCalls => [.. _domesticCalls];

    public int DomesticCallCount(string prefix) => _domesticCalls.Count(call => call.StartsWith(prefix, StringComparison.Ordinal));

    public void RaiseDomesticConnection(int code) => DomesticConnection?.Invoke(code);

    public void RaiseDomesticKLine(string code, string data) => DomesticKLine?.Invoke(code, data);

    public void RaiseDomesticKLineComplete() => DomesticKLineComplete?.Invoke("##");

    public SkOpenFailure? OpenDomestic()
    {
        _domesticCalls.Enqueue("open");

        if (DomesticOpenThrows)
        {
            throw new InvalidOperationException("no connection point");
        }

        return DomesticOpenFailure;
    }

    public int EnterDomesticMonitor()
    {
        _domesticCalls.Enqueue("enter");

        if (DomesticEnterCode == 0 && DomesticAutoReady)
        {
            Later(() =>
            {
                DomesticConnection?.Invoke(3001);
                DomesticConnection?.Invoke(3003);
            });
        }

        return DomesticEnterCode;
    }

    public int LeaveDomesticMonitor()
    {
        _domesticCalls.Enqueue("leave");
        return 0;
    }

    public int RequestDomesticKLine(string stockNo, short kLineType, short outType, short tradeSession, string startDate, string endDate, short minuteNumber)
    {
        _domesticCalls.Enqueue($"kline {stockNo} {kLineType} {outType} {tradeSession} {startDate} {endDate} {minuteNumber}");

        if (DomesticKLineCode == 0)
        {
            (string Code, string Data)[] rows = [.. DomesticKLineRows];
            bool complete = DomesticAutoComplete;
            Later(() =>
            {
                foreach ((string code, string data) in rows)
                {
                    DomesticKLine?.Invoke(code, data);
                }

                if (complete)
                {
                    DomesticKLineComplete?.Invoke("##");
                }
            });
        }

        return DomesticKLineCode;
    }

    public void Dispose()
    {
    }

    /// <summary>事件一律非同步送出(真 SKCOM 走訊息迴圈,不在呼叫裡觸發)。Events are always asynchronous.</summary>
    private static void Later(Action action) => _ = Task.Run(async () =>
    {
        await Task.Delay(20);
        action();
    });
}
