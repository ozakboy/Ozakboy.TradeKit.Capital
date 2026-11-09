using System.Globalization;
using System.Runtime.InteropServices;
using Ozakboy.TradeKit.Capital.Host.Interop;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>C. 國內期貨 / 選擇權即時報價。C. Domestic futures and options live quotes.</summary>
internal sealed partial class FullProbeRunner
{
    private const int DomesticReady = 3003;
    private const short FuturesMarket = 2;
    private const short OptionsMarket = 3;
    private const int MaxQuoteRecords = 60;
    private const int MaxBookRecords = 200;
    private const int MaxLiveKLineRecords = 2000;

    private const string TickHeader = "market\tindex\tptr\tdate\ttime_hms\ttime_millis_micros\tbid\task\tclose\tqty\tsimulate\tH=回補/L=即時\treceived_utc";

    // 2026-11-07 probe-tw 實測的近月代碼(台指、小台、微台)
    private static readonly string[] DomesticFutures = ["TX00", "MTX00", "TM0000"];
    private static readonly short[] StockListMarkets = [FuturesMarket, OptionsMarket];

    private readonly Dictionary<(short Market, int Index), string> _domesticCodes = [];
    private readonly Dictionary<string, SKSTOCKLONG> _domesticInfo = new(StringComparer.Ordinal);
    private readonly Dictionary<(short Market, int Index), int> _domesticQuoteCounts = [];
    private readonly Dictionary<string, int> _bookCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _tickCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<short, List<string>> _stockListChunks = [];
    private readonly HashSet<short> _stockListDone = [];
    private ISKQuoteLib? _domestic;
    private bool _domesticReady;
    private DateTime _lastStockListUtc = DateTime.MinValue;

    private async Task StartDomesticAsync()
    {
        if (_domestic is null)
        {
            return;
        }

        ProbeLog.Say("連線國內報價主機(等 3003 商品基本資料下載完成,最多 60 秒)…");
        int enter = _domestic.SKQuoteLib_EnterMonitorLONG();
        Report("tw_enter_monitor", enter);

        if (enter != 0)
        {
            ProbeLog.Say($"國內報價連線被拒(代碼 {enter}),國內報價跳過。");
            return;
        }

        if (!await WaitUntilAsync(() => _domesticReady, ConnectLimit).ConfigureAwait(true))
        {
            ProbeLog.Say("60 秒內沒收到 3003,照樣繼續,看看結果。");
        }

        foreach (short market in StockListMarkets)
        {
            ProbeLog.Say($"下載國內商品清單(市場 {market})…");
            Report("tw_request_stock_list", _domestic.SKQuoteLib_RequestStockList(market), ("market", market));
            await WaitQuietAsync(() => _lastStockListUtc, () => _stockListDone.Contains(market), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(120)).ConfigureAwait(true);
            _log.Event("tw_stock_list_done", new Dictionary<string, object?>
            {
                ["market"] = market,
                ["chunks"] = _stockListChunks.TryGetValue(market, out List<string>? chunks) ? chunks.Count : 0,
                ["end_marker"] = _stockListDone.Contains(market),
            });
        }

        // 先以基本資料拿到索引(訂閱後事件只給市場別 + 索引)與台指期的參考價,再挑選擇權;四檔一次訂(手冊:一個物件只有一組即時報價訂閱)
        List<(short Market, string Code)> symbols = [.. DomesticFutures.Select(code => (FuturesMarket, code))];

        foreach ((short market, string code) in symbols)
        {
            ReadDomesticInfo(market, code);
        }

        string? option = PickDomesticOption();

        if (option is not null)
        {
            ReadDomesticInfo(OptionsMarket, option);
            symbols.Add((OptionsMarket, option));
        }

        string request = string.Join(',', symbols.Select(symbol => symbol.Code));

        // 手冊 4-4-2:psPageNo 自 V2.13.54 起「請固定帶 1」;被拒才改用舊版的 -1
        short page = 1;
        int stocks = _domestic.SKQuoteLib_RequestStocks(ref page, request);
        Report("tw_request_stocks", stocks, ("page_in", 1), ("page_out", page), ("symbols", request));

        if (stocks != 0)
        {
            page = -1;
            stocks = _domestic.SKQuoteLib_RequestStocks(ref page, request);
            Report("tw_request_stocks_retry", stocks, ("page_in", -1), ("page_out", page), ("symbols", request));
        }

        // 手冊 4-4-3:psPageNo「請從 0 開始」,一個 Page 一檔
        short tickPage = 0;

        foreach ((short _, string code) in symbols)
        {
            short pageIn = tickPage;
            short pageRef = tickPage;
            Report("tw_request_ticks", _domestic.SKQuoteLib_RequestTicks(ref pageRef, code), ("symbol", code), ("page_in", pageIn), ("page_out", pageRef));
            tickPage++;
        }

        ProbeLog.Say($"國內已訂閱:{request}");
    }

    private void ReadDomesticInfo(short market, string code)
    {
        SKSTOCKLONG item = default;
        int result = _domestic!.SKQuoteLib_GetStockByMarketAndNo(market, code, ref item);
        string source = "GetStockByMarketAndNo";

        if (result != 0)
        {
            item = default;
            result = _domestic.SKQuoteLib_GetStockByNoLONG(code, ref item);
            source = "GetStockByNoLONG";
        }

        Dictionary<string, object?> fields = Fields(item);
        fields["code"] = result;
        fields["message"] = Safe(() => Center.SKCenterLib_GetReturnCodeMessage(result));
        fields["request_market"] = market;
        fields["request_code"] = code;
        fields["source"] = source;
        _log.Event("tw_stock_info", fields);

        if (result == 0)
        {
            _domesticCodes[(market, item.nStockIdx)] = code;
            _domesticInfo[code] = item;
        }
    }

    private string? PickDomesticOption()
    {
        decimal? underlying = null;

        if (_domesticInfo.TryGetValue(DomesticFutures[0], out SKSTOCKLONG tx))
        {
            int raw = tx.nClose != 0 ? tx.nClose : tx.nRef;

            if (raw != 0 && tx.sDecimal is >= 0 and <= 9)
            {
                underlying = raw / Pow10(tx.sDecimal);
            }
        }

        IReadOnlyList<StockListEntry> entries = ProbeSymbolPicker.ParseStockList(_stockListChunks.TryGetValue(OptionsMarket, out List<string>? chunks) ? chunks : []);
        int today = int.Parse(DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        StockListEntry? chosen = ProbeSymbolPicker.PickTaiexCall(entries, underlying, today, out string reason);
        _log.Event("tw_option_chosen", new Dictionary<string, object?>
        {
            ["entries"] = entries.Count,
            ["underlying"] = underlying,
            ["code"] = chosen?.Code,
            ["name"] = chosen?.Name,
            ["order_id"] = chosen?.OrderId,
            ["last_trade_day"] = chosen?.LastTradeDay,
            ["reason"] = reason,
        });
        ProbeLog.Say(chosen is null ? "選擇權清單是空的,不訂選擇權。" : $"選擇權:{chosen.Code} {chosen.Name}({reason})");
        return chosen?.Code;
    }

    private static decimal Pow10(int exponent)
    {
        decimal value = 1m;

        for (int i = 0; i < exponent; i++)
        {
            value *= 10m;
        }

        return value;
    }

    private string DomesticCode(short market, int index) =>
        _domesticCodes.TryGetValue((market, index), out string? code)
            ? code
            : string.Create(CultureInfo.InvariantCulture, $"m{market}-i{index}");

    /// <summary>計數並回傳是否還在上限內。Counts and tells whether the limit still allows recording.</summary>
    private static bool Within(Dictionary<string, int> counts, string key, int limit)
    {
        int count = counts.TryGetValue(key, out int previous) ? previous + 1 : 1;
        counts[key] = count;
        return count <= limit;
    }

    private void HookDomesticEvents(QuoteEventsSink sink)
    {
        sink.Connection += (kind, code) =>
        {
            _log.Event("tw_on_connection", new Dictionary<string, object?> { ["conn_kind"] = kind, ["code"] = code });

            if (kind == DomesticReady)
            {
                _domesticReady = true;
            }
        };

        sink.StockList += (market, data) =>
        {
            _lastStockListUtc = DateTime.UtcNow;
            _log.Raw("tw-stocklist-" + market.ToString(CultureInfo.InvariantCulture), data);

            if (!_stockListChunks.TryGetValue(market, out List<string>? chunks))
            {
                chunks = [];
                _stockListChunks[market] = chunks;
            }

            chunks.Add(data);

            if (data.TrimStart().StartsWith("##", StringComparison.Ordinal))
            {
                _stockListDone.Add(market);
            }
        };

        sink.Quote += (market, index) =>
        {
            (short, int) key = (market, index);
            int count = _domesticQuoteCounts.TryGetValue(key, out int previous) ? previous + 1 : 1;
            _domesticQuoteCounts[key] = count;

            if (count > MaxQuoteRecords || _domestic is null)
            {
                return;
            }

            SKSTOCKLONG item = default;
            int result = _domestic.SKQuoteLib_GetStockByIndexLONG(market, index, ref item);
            Dictionary<string, object?> fields = Fields(item);
            fields["code"] = result;
            fields["event_market"] = market;
            fields["event_index"] = index;
            fields["symbol"] = DomesticCode(market, index);
            fields["received_utc"] = Stamp();
            _log.Event("tw_quote", fields);
        };

        sink.Tick += tick =>
        {
            string code = DomesticCode(tick.MarketNo, tick.StockIdx);
            Within(_tickCounts, "tw-" + code, int.MaxValue);
            RawWithHeader(
                "tw-ticks-" + FileSafe(code),
                TickHeader,
                string.Join(
                    '\t',
                    Invariant(tick.MarketNo),
                    Invariant(tick.StockIdx),
                    Invariant(tick.Ptr),
                    Invariant(tick.Date),
                    tick.TimeHms.ToString("D6", CultureInfo.InvariantCulture),
                    Invariant(tick.TimeMillisMicros),
                    Invariant(tick.Bid),
                    Invariant(tick.Ask),
                    Invariant(tick.Close),
                    Invariant(tick.Qty),
                    Invariant(tick.Simulate),
                    tick.History ? "H" : "L",
                    Stamp()));
        };

        sink.Best5 += book =>
        {
            string code = DomesticCode(book.MarketNo, book.StockIdx);

            if (Within(_bookCounts, "tw-best5-" + code, MaxBookRecords))
            {
                RawWithHeader(
                    "tw-best5-" + FileSafe(code),
                    "market\tindex\t" + string.Join('\t', SkBookFields.DomesticBest5) + "\treceived_utc",
                    Invariant(book.MarketNo) + "\t" + Invariant(book.StockIdx) + "\t" + string.Join('\t', book.Values.Select(Invariant)) + "\t" + Stamp());
            }
        };

        sink.LiveKLine += bar =>
        {
            string code = bar.StockNo.Length > 0 ? bar.StockNo : "unknown";

            if (Within(_bookCounts, "tw-livek-" + code, MaxLiveKLineRecords))
            {
                RawWithHeader(
                    "tw-livek-" + FileSafe(code),
                    "type\tstock_no\tdate\ttime_hm\topen\thigh\tlow\tclose\tqty\treceived_utc",
                    string.Join(
                        '\t',
                        Invariant(bar.Type),
                        bar.StockNo,
                        Invariant(bar.Date),
                        Invariant(bar.TimeHm),
                        Invariant(bar.Open),
                        Invariant(bar.High),
                        Invariant(bar.Low),
                        Invariant(bar.Close),
                        Invariant(bar.Qty),
                        Stamp()));
            }
        };
    }
}
