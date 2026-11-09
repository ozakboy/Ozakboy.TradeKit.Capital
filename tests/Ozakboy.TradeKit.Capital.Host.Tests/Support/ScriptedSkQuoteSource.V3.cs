using System.Collections.Concurrent;
using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Host.Tests.Support;

// 0.3.0 的成員:版本、國內即時報價、海外十檔、海外選擇權、帳務查詢。預設行為依手冊 V2.13.59 推定(待實測):
// 國內訂閱以 GetStockByMarketAndNo 查得到的代碼才算存在;海外選擇權 EnterMonitorLONG 後送 3001;帳務查詢以「##」與狀態事件結束。
// The 0.3.0 members. Defaults follow the V2.13.59 manual and are still to be measured.
internal sealed partial class ScriptedSkQuoteSource
{
    private readonly ConcurrentQueue<string> _liveCalls = new();
    private readonly ConcurrentQueue<string> _optionCalls = new();
    private readonly ConcurrentQueue<(string Call, long Timestamp)> _accountCalls = new();

    public event Action<short, string>? DomesticStockList;

    public event Action<short, int>? DomesticQuote;

    public event Action<SkDomesticTick>? DomesticTick;

    public event Action<SkBook>? DomesticBest5;

    public event Action<SkLiveBar>? DomesticLiveBar;

    public event Action<SkBook>? OverseasBest5;

    public event Action<SkBook>? OverseasBest10;

    public event Action<int>? OverseasOptionConnection;

    public event Action<string>? OverseasOptionProduct;

    public event Action<int>? OverseasOptionQuote;

    public event Action<SkTickData>? OverseasOptionTick;

    public event Action<SkBook>? OverseasOptionBest5;

    public event Action<SkBook>? OverseasOptionBest10;

    public event Action<string, string>? AccountRow;

    public event Action<string, string>? AccountReply;

    public event Action<string, int, string>? AccountStatus;

    // ── 版本 ──

    public string ApiVersion { get; set; } = "2.13.59_x64";

    public bool ApiVersionThrows { get; set; }

    public string GetApiVersion(string loginId)
    {
        _liveCalls.Enqueue("version " + loginId);

        if (ApiVersionThrows)
        {
            throw new InvalidOperationException("no version");
        }

        return ApiVersion;
    }

    // ── 國內即時 ──

    /// <summary>代碼 → (市場別, 索引, 小數位數);GetStockByMarketAndNo 只認這裡的。Code to market, index and decimals.</summary>
    public ConcurrentDictionary<string, (short Market, int Index, int Decimals)> DomesticStocks { get; } = new(StringComparer.Ordinal)
    {
        ["TX00"] = (2, 10, 2),
        ["MTX00"] = (2, 11, 2),
        ["TXO39800K6"] = (3, 10, 1),
    };

    public int DomesticStocksCode { get; set; }

    public int DomesticTicksCode { get; set; }

    /// <summary>RequestStockList 之後自動送的原文(市場別, 原文)。Chunks sent after RequestStockList.</summary>
    public List<(short Market, string Data)> StockListChunks { get; } =
    [
        (3, "TXO1,別的市場,0,TXO1,1,NTD;"),
        (2, "TX00,台指近,20261118,TXFK6,1,NTD;MTX00,小台近,20261118,MXFK6,1,NTD;"),
        (2, "##"),
    ];

    public string[] LiveCalls => [.. _liveCalls];

    public int LiveCallCount(string prefix) => _liveCalls.Count(call => call.StartsWith(prefix, StringComparison.Ordinal));

    public void RaiseDomesticQuote(short market, int index) => DomesticQuote?.Invoke(market, index);

    public void RaiseDomesticTick(SkDomesticTick tick) => DomesticTick?.Invoke(tick);

    public void RaiseDomesticBest5(SkBook book) => DomesticBest5?.Invoke(book);

    public void RaiseDomesticLiveBar(SkLiveBar bar) => DomesticLiveBar?.Invoke(bar);

    public void RaiseOverseasBest5(SkBook book) => OverseasBest5?.Invoke(book);

    public void RaiseOverseasBest10(SkBook book) => OverseasBest10?.Invoke(book);

    public int RequestDomesticStockList(short market)
    {
        _liveCalls.Enqueue("tw_stocklist " + market.ToString(CultureInfo.InvariantCulture));
        (short Market, string Data)[] chunks = [.. StockListChunks];
        Later(() =>
        {
            foreach ((short chunkMarket, string data) in chunks)
            {
                DomesticStockList?.Invoke(chunkMarket, data);
            }
        });
        return 0;
    }

    public int RequestDomesticStocks(string codes)
    {
        _liveCalls.Enqueue("tw_stocks " + codes);
        return DomesticStocksCode;
    }

    public int RequestDomesticTicks(short page, string code)
    {
        _liveCalls.Enqueue(string.Create(CultureInfo.InvariantCulture, $"tw_ticks {page} {code}"));
        return DomesticTicksCode;
    }

    public int GetDomesticStockByIndex(short market, int index, out SkDomesticStock stock)
    {
        _liveCalls.Enqueue(string.Create(CultureInfo.InvariantCulture, $"tw_index {market} {index}"));

        foreach ((string code, (short Market, int Index, int Decimals) value) in DomesticStocks)
        {
            if (value.Market == market && value.Index == index)
            {
                stock = Stock(code, value.Index, value.Decimals);
                return 0;
            }
        }

        stock = default;
        return 3023;
    }

    public int GetDomesticStockByCode(short market, string code, out SkDomesticStock stock)
    {
        _liveCalls.Enqueue(string.Create(CultureInfo.InvariantCulture, $"tw_lookup {market} {code}"));

        if (DomesticStocks.TryGetValue(code, out (short Market, int Index, int Decimals) value) && value.Market == market)
        {
            stock = Stock(code, value.Index, value.Decimals);
            return 0;
        }

        stock = default;
        return 3023;
    }

    // ── 海外十檔 ──

    public int DepthCode { get; set; }

    public int RequestMarketDepth(string stockNo)
    {
        _calls.Enqueue("depth " + stockNo);
        return DepthCode;
    }

    // ── 海外選擇權 ──

    public SkOpenFailure? OptionsOpenFailure { get; set; }

    public int OptionsEnterCode { get; set; }

    /// <summary>EnterMonitorLONG 後自動送 3001(推定,待實測)。Whether 3001 follows automatically.</summary>
    public bool OptionsAutoReady { get; set; } = true;

    public string[]? OptionProductRows { get; set; } = ["CME,芝加哥商品交易所,NQ2612C25000,小那買權,20261218", "##,,,,"];

    public ConcurrentDictionary<int, SkOptionQuote> OptionQuotes { get; } = new();

    public string[] OptionCalls => [.. _optionCalls];

    public int OptionCallCount(string prefix) => _optionCalls.Count(call => call.StartsWith(prefix, StringComparison.Ordinal));

    public void RaiseOptionConnection(int code) => OverseasOptionConnection?.Invoke(code);

    public void RaiseOptionQuote(int index) => OverseasOptionQuote?.Invoke(index);

    public void RaiseOptionTick(SkTickData tick) => OverseasOptionTick?.Invoke(tick);

    public void RaiseOptionBest5(SkBook book) => OverseasOptionBest5?.Invoke(book);

    public void RaiseOptionBest10(SkBook book) => OverseasOptionBest10?.Invoke(book);

    public SkOpenFailure? OpenOverseasOptions()
    {
        _optionCalls.Enqueue("open");
        return OptionsOpenFailure;
    }

    public int EnterOverseasOptionMonitor()
    {
        _optionCalls.Enqueue("enter");

        if (OptionsEnterCode == 0 && OptionsAutoReady)
        {
            Later(() => OverseasOptionConnection?.Invoke(3001));
        }

        return OptionsEnterCode;
    }

    public int LeaveOverseasOptionMonitor()
    {
        _optionCalls.Enqueue("leave");
        return 0;
    }

    public int RequestOverseasOptionProducts()
    {
        _optionCalls.Enqueue("products");

        if (OptionProductRows is { } rows)
        {
            Later(() =>
            {
                foreach (string row in rows)
                {
                    OverseasOptionProduct?.Invoke(row);
                }
            });
        }

        return 0;
    }

    public int RequestOverseasOptionStocks(string stockNos)
    {
        _optionCalls.Enqueue("stocks " + stockNos);
        return 0;
    }

    public int RequestOverseasOptionTicks(string stockNo)
    {
        _optionCalls.Enqueue("ticks " + stockNo);
        return 0;
    }

    public int RequestOverseasOptionMarketDepth(string stockNo)
    {
        _optionCalls.Enqueue("depth " + stockNo);
        return 0;
    }

    public int GetOverseasOptionStockByIndex(int index, out SkOptionQuote quote) =>
        OptionQuotes.TryGetValue(index, out quote) ? 0 : 1;

    // ── 帳務 ──

    public SkOpenFailure? AccountsOpenFailure { get; set; }

    public int AccountsInitCode { get; set; }

    public int AccountsListCode { get; set; }

    public int CertificateCode { get; set; }

    public int QueryCode { get; set; }

    /// <summary>GetUserAccount 之後自動送的 OnAccount 原文。The OnAccount rows sent after GetUserAccount.</summary>
    public List<string> AccountRows { get; } = ["TF,F020000,群益期貨,1234567,A123456789,王小明", "OF,F020000,群益期貨,7654321,A123456789,王小明"];

    /// <summary>每個查詢事件名稱之後自動送的回覆(結果事件, 原文)與狀態。Replies sent after each query.</summary>
    public List<(string Event, string Data)> QueryReplies { get; } = [];

    /// <summary>查詢狀態事件(事件名稱, 狀態, 訊息);null = 不送。The status event, or none.</summary>
    public (string Event, int Status, string Message)? QueryStatus { get; set; }

    public string ReportText { get; set; } = "TF,FUT,TAIFEX\r\nTF,FUT,TAIFEX";

    public (string Call, long Timestamp)[] AccountCalls => [.. _accountCalls];

    public void RaiseAccountReply(string eventName, string data) => AccountReply?.Invoke(eventName, data);

    public int AccountCallCount(string prefix) => _accountCalls.Count(call => call.Call.StartsWith(prefix, StringComparison.Ordinal));

    public SkOpenFailure? OpenAccounts()
    {
        Account("open");
        return AccountsOpenFailure;
    }

    public int InitializeAccounts()
    {
        Account("init");
        return AccountsInitCode;
    }

    public int RequestAccounts()
    {
        Account("accounts");

        if (AccountsListCode == 0)
        {
            string[] rows = [.. AccountRows];
            Later(() =>
            {
                foreach (string row in rows)
                {
                    AccountRow?.Invoke("A123456789", row);
                }
            });
        }

        return AccountsListCode;
    }

    public int ReadCertificate(string loginId)
    {
        Account("cert " + loginId);
        return CertificateCode;
    }

    public int RequestFutureRights(string loginId, string account, short coinType) =>
        Query(string.Create(CultureInfo.InvariantCulture, $"rights {account} {coinType}"));

    public int RequestOpenInterest(string loginId, string account, int format) =>
        Query(string.Create(CultureInfo.InvariantCulture, $"open_interest {account} {format}"));

    public int RequestOverseaFutureRights(string loginId, string account) => Query("os_rights " + account);

    public int RequestOverseaOpenInterest(string loginId, string account, int format) =>
        Query(string.Create(CultureInfo.InvariantCulture, $"os_open_interest {account} {format}"));

    public string GetOrderReport(string loginId, string account, int format)
    {
        Account(string.Create(CultureInfo.InvariantCulture, $"order_report {account} {format}"));
        return ReportText;
    }

    public string GetFulfillReport(string loginId, string account, int format)
    {
        Account(string.Create(CultureInfo.InvariantCulture, $"fulfill_report {account} {format}"));
        return ReportText;
    }

    private int Query(string call)
    {
        Account(call);

        if (QueryCode == 0)
        {
            (string Event, string Data)[] replies = [.. QueryReplies];
            (string Event, int Status, string Message)? status = QueryStatus;
            Later(() =>
            {
                foreach ((string name, string data) in replies)
                {
                    AccountReply?.Invoke(name, data);
                }

                if (status is { } value)
                {
                    AccountStatus?.Invoke(value.Event, value.Status, value.Message);
                }
            });
        }

        return QueryCode;
    }

    private void Account(string call) => _accountCalls.Enqueue((call, System.Diagnostics.Stopwatch.GetTimestamp()));

    private static SkDomesticStock Stock(string code, int index, int decimals) => new()
    {
        Index = index,
        Decimals = decimals,
        MarketNo = "2",
        StockNo = code,
        StockName = code + " 名稱",
        StockNoSpread = string.Empty,
        Open = 3970000,
        High = 3990000,
        Low = 3960000,
        Close = 3978600,
        Reference = 3975000,
        TradingDay = 20261106,
        DealTime = 84601,
    };
}
