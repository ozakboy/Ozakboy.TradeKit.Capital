using System.Globalization;
using Ozakboy.TradeKit.Capital.Host.Interop;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>D. 海外五檔 / 十檔;E. 海外選擇權報價;收集結束後的摘要與離線。D. Overseas depth; E. overseas options; summary and leave.</summary>
internal sealed partial class FullProbeRunner
{
    private const int OverseasConnected = 3001;
    private const int OverseasReady = 3003;
    private const int OverseasOptionCount = 2;

    private const string NasdaqFutures = "CME,NQ0000";

    // 既有探針與 serve 實測過的訂閱字串(交易所,代碼)
    private static readonly string[] OverseasSymbols = ["FX,SUSDTWD", NasdaqFutures];

    private readonly Dictionary<int, string> _overseasCodes = [];
    private readonly Dictionary<int, string> _overseasOptionCodes = [];
    private readonly Dictionary<int, int> _overseasOptionQuoteCounts = [];
    private readonly List<string> _overseasOptionProducts = [];
    private ISKOSQuoteLib? _overseas;
    private ISKOOQuoteLib? _overseasOption;
    private bool _overseasConnected;
    private bool _overseasOptionConnected;
    private bool _overseasOptionProductsDone;
    private DateTime _lastOverseasOptionProductUtc = DateTime.MinValue;
    private int _overseasTicks;
    private int _overseasQuotes;

    private async Task StartOverseasAsync()
    {
        if (_overseas is null)
        {
            return;
        }

        ProbeLog.Say("連線海外期貨報價主機…");
        Report("os_enter_monitor", _overseas.SKOSQuoteLib_EnterMonitorLONG());

        if (!await WaitUntilAsync(() => _overseasConnected, ConnectLimit).ConfigureAwait(true))
        {
            ProbeLog.Say("60 秒內沒收到海外報價主機的 3001,海外五檔 / 十檔跳過。");
            return;
        }

        foreach (string symbol in OverseasSymbols)
        {
            int result = _overseas.SKOSQuoteLib_GetStockByNoNineDigitLONG(symbol, out SKFOREIGN_9LONG item);
            Dictionary<string, object?> fields = Fields(item);
            fields["code"] = result;
            fields["request_symbol"] = symbol;
            _log.Event("os_stock_info", fields);

            if (result == 0)
            {
                _overseasCodes[item.nStockIdx] = symbol;
            }
        }

        short page = -1;
        Report("os_request_stocks", _overseas.SKOSQuoteLib_RequestStocks(ref page, string.Join('#', OverseasSymbols)), ("page_out", page));

        foreach (string symbol in OverseasSymbols)
        {
            short tickPage = -1;
            Report("os_request_ticks", _overseas.SKOSQuoteLib_RequestTicks(ref tickPage, symbol), ("symbol", symbol), ("page_out", tickPage));
        }

        foreach (string symbol in OverseasSymbols)
        {
            short depthPage = -1;
            Report("os_request_market_depth", _overseas.SKOSQuoteLib_RequestMarketDepth(ref depthPage, symbol), ("symbol", symbol), ("page_out", depthPage));
        }

        ProbeLog.Say($"海外已訂閱五檔 / 十檔:{string.Join(" / ", OverseasSymbols)}");
    }

    private async Task StartOverseasOptionAsync()
    {
        if (_overseasOption is null)
        {
            return;
        }

        ProbeLog.Say("連線海外選擇權報價主機…");
        Report("oo_enter_monitor", _overseasOption.SKOOQuoteLib_EnterMonitorLONG());

        if (!await WaitUntilAsync(() => _overseasOptionConnected, ConnectLimit).ConfigureAwait(true))
        {
            ProbeLog.Say("60 秒內沒收到海外選擇權報價主機的 3001 / 3003,照樣試著下載商品清單。");
        }

        // 收到連線後再給 3 秒,讓後續的連線代碼先到
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
        Report("oo_is_connected", _overseasOption.SKOOQuoteLib_IsConnected());

        ProbeLog.Say("下載海外選擇權商品清單…");
        Report("oo_request_products", _overseasOption.SKOOQuoteLib_RequestProducts());
        await WaitQuietAsync(() => _lastOverseasOptionProductUtc, () => _overseasOptionProductsDone, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(120)).ConfigureAwait(true);
        _log.Event("oo_products_done", new Dictionary<string, object?> { ["count"] = _overseasOptionProducts.Count, ["end_marker"] = _overseasOptionProductsDone });

        // 2026-11-09 照清單順序挑到履約價 100 的深度價內 / 價外,三分鐘沒有成交;改挑最接近小那斯達克期貨價格的買權與賣權。
        // The first run picked a 100 strike that never traded; the call and put nearest the Nasdaq futures price are picked now.
        decimal? underlying = ReadOverseasPrice(NasdaqFutures);
        int today = int.Parse(DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        IReadOnlyList<(string Request, string Line)> chosen = ProbeSymbolPicker.PickOverseasOptions(_overseasOptionProducts, OverseasOptionCount, underlying, today);
        _log.Event("oo_symbols_chosen", new Dictionary<string, object?>
        {
            ["underlying"] = underlying,
            ["requests"] = chosen.Select(item => item.Request).ToArray(),
            ["lines"] = chosen.Select(item => item.Line).ToArray(),
        });

        if (chosen.Count == 0)
        {
            ProbeLog.Say("海外選擇權商品清單是空的,不訂閱。");
            return;
        }

        foreach ((string request, string _) in chosen)
        {
            SKFOREIGNLONG item = default;
            int result = _overseasOption.SKOOQuoteLib_GetStockByNoLONG(request, ref item);
            Dictionary<string, object?> fields = Fields(item);
            fields["code"] = result;
            fields["request_symbol"] = request;
            _log.Event("oo_stock_info", fields);

            if (result == 0)
            {
                _overseasOptionCodes[item.nStockIdx] = request;
            }
        }

        // 手冊沒有 SKOOQuoteLib 的章節:比照 SKOSQuoteLib 以 # 串接、頁碼 -1;被拒再試逗號
        string symbols = string.Join('#', chosen.Select(item => item.Request));
        short page = -1;
        int stocks = _overseasOption.SKOOQuoteLib_RequestStocks(ref page, symbols);
        Report("oo_request_stocks", stocks, ("symbols", symbols), ("page_out", page));

        if (stocks != 0)
        {
            symbols = string.Join(',', chosen.Select(item => item.Request));
            page = -1;
            Report("oo_request_stocks_retry", _overseasOption.SKOOQuoteLib_RequestStocks(ref page, symbols), ("symbols", symbols), ("page_out", page));
        }

        foreach ((string request, string _) in chosen)
        {
            short tickPage = -1;
            Report("oo_request_ticks", _overseasOption.SKOOQuoteLib_RequestTicks(ref tickPage, request), ("symbol", request), ("page_out", tickPage));
            short depthPage = -1;
            Report("oo_request_market_depth", _overseasOption.SKOOQuoteLib_RequestMarketDepth(ref depthPage, request), ("symbol", request), ("page_out", depthPage));
        }

        ProbeLog.Say($"海外選擇權已訂閱:{string.Join(" / ", chosen.Select(item => item.Request))}");
    }

    private void LogQuoteSummary()
    {
        _log.Event("quote_summary", new Dictionary<string, object?>
        {
            ["tw_ticks"] = _tickCounts,
            ["books_and_livek"] = _bookCounts,
            ["tw_quotes"] = _domesticQuoteCounts.Count,
            ["os_ticks"] = _overseasTicks,
            ["os_quotes"] = _overseasQuotes,
            ["oo_quotes"] = _overseasOptionQuoteCounts.Count,
        });
    }

    private void LeaveQuoteMonitors()
    {
        if (_overseasOption is not null)
        {
            Report("oo_leave_monitor", _overseasOption.SKOOQuoteLib_LeaveMonitor());
        }

        if (_overseas is not null)
        {
            Report("os_leave_monitor", _overseas.SKOSQuoteLib_LeaveMonitor());
        }

        if (_domestic is not null)
        {
            Report("tw_leave_monitor", _domestic.SKQuoteLib_LeaveMonitor());
        }
    }

    /// <summary>
    /// 海期目前價格(成交價,沒有就參考價)÷ 10^小數位數;讀不到為 null。只讀報價(<c>GetStockByNoNineDigitLONG</c>)。
    /// The overseas futures price (last, else reference); null when unavailable. A read-only quote lookup.
    /// </summary>
    private decimal? ReadOverseasPrice(string symbol)
    {
        if (_overseas is null || _overseas.SKOSQuoteLib_GetStockByNoNineDigitLONG(symbol, out SKFOREIGN_9LONG item) != 0)
        {
            return null;
        }

        long raw = item.nClose != 0 ? item.nClose : item.nRef;
        return raw > 0 && item.sDecimal is >= 0 and <= 9 ? raw / Pow10(item.sDecimal) : null;
    }

    private static string Lookup(Dictionary<int, string> codes, int index) =>
        codes.TryGetValue(index, out string? code) ? code : string.Create(CultureInfo.InvariantCulture, $"idx{index}");

    private void RecordBook(string prefix, Dictionary<int, string> codes, SkBookEvent book, IReadOnlyList<string> names)
    {
        string code = Lookup(codes, book.StockIdx);

        if (Within(_bookCounts, prefix + code, MaxBookRecords))
        {
            RawWithHeader(
                prefix + FileSafe(code),
                "index\t" + string.Join('\t', names) + "\treceived_utc",
                Invariant(book.StockIdx) + "\t" + string.Join('\t', book.Values.Select(Invariant)) + "\t" + Stamp());
        }
    }

    private void HookOverseasEvents(OsQuoteEventsSink sink)
    {
        sink.Connected += (code, socketCode) =>
        {
            _log.Event("os_on_connect", new Dictionary<string, object?> { ["code"] = code, ["socket_code"] = socketCode });

            if (code is OverseasConnected or OverseasReady)
            {
                _overseasConnected = true;
            }
        };

        // 報價與成交既有探針測過,這裡只計數
        sink.Quote += _ => _overseasQuotes++;
        sink.Tick += (_, _, _, _, _, _, _) => _overseasTicks++;
        sink.Best5 += book => RecordBook("os-best5-", _overseasCodes, book, SkBookFields.Best5);
        sink.Best10 += book => RecordBook("os-best10-", _overseasCodes, book, SkBookFields.Best10);
    }

    private void HookOverseasOptionEvents(OoQuoteEventsSink sink)
    {
        sink.Connected += (code, socketCode) =>
        {
            _log.Event("oo_on_connect", new Dictionary<string, object?> { ["code"] = code, ["socket_code"] = socketCode });

            if (code is OverseasConnected or OverseasReady)
            {
                _overseasOptionConnected = true;
            }
        };

        sink.Product += value =>
        {
            _lastOverseasOptionProductUtc = DateTime.UtcNow;
            _log.Raw("oo-products", value);

            if (value.TrimStart().StartsWith("##", StringComparison.Ordinal))
            {
                _overseasOptionProductsDone = true;
            }
            else
            {
                _overseasOptionProducts.Add(value);
            }
        };

        sink.Quote += index =>
        {
            int count = _overseasOptionQuoteCounts.TryGetValue(index, out int previous) ? previous + 1 : 1;
            _overseasOptionQuoteCounts[index] = count;

            if (count > MaxQuoteRecords || _overseasOption is null)
            {
                return;
            }

            SKFOREIGNLONG item = default;
            int result = _overseasOption.SKOOQuoteLib_GetStockByIndexLONG(index, ref item);
            Dictionary<string, object?> fields = Fields(item);
            fields["code"] = result;
            fields["event_index"] = index;
            fields["symbol"] = Lookup(_overseasOptionCodes, index);
            fields["received_utc"] = Stamp();
            _log.Event("oo_quote", fields);
        };

        sink.Tick += tick =>
        {
            string code = Lookup(_overseasOptionCodes, tick.StockIdx);
            Within(_tickCounts, "oo-" + code, int.MaxValue);
            RawWithHeader(
                "oo-ticks-" + FileSafe(code),
                "index\tptr\tdate\ttime\tclose\tqty\tH=回補/L=即時\treceived_utc",
                string.Join(
                    '\t',
                    Invariant(tick.StockIdx),
                    Invariant(tick.Ptr),
                    Invariant(tick.Date),
                    Invariant(tick.Time),
                    Invariant(tick.Close),
                    Invariant(tick.Qty),
                    tick.History ? "H" : "L",
                    Stamp()));
        };

        sink.Best5 += book => RecordBook("oo-best5-", _overseasOptionCodes, book, SkBookFields.Best5);
        sink.Best10 += book => RecordBook("oo-best10-", _overseasOptionCodes, book, SkBookFields.Best10);
    }
}
