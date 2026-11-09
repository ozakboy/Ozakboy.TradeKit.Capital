using System.Collections.Concurrent;
using System.Globalization;
using Ozakboy.TradeKit.Capital.Host.Serve;

namespace FakeCapitalHostExe;

// 0.3.0:SKCOM 版本、國內即時報價、海外十檔。行為依手冊 V2.13.59(章節號在各處),實測 2026-11-09 的部分照實測:商品清單 6 欄與 AM 代碼、
// 結尾「##,,;」、訂閱後第一則報價只填一部分(其餘 0)、成交毫秒精度(微秒後三位 000)、即時分 K 的 HHmm 是結束時刻。價格與代碼是假的。
// 0.3.0: the version, domestic live quotes and the overseas best 10; the parts measured on 2026-11-09 copy the
// measurement (list format and AM codes, a partial first quote, millisecond ticks, end-of-minute live bars). Prices
// and codes are made up.
internal sealed partial class FakeSkQuoteSource
{
    private const int CodeStockNotFound = 3023;
    private const int CodeTickStockNotFound = 3010;

    private readonly ConcurrentDictionary<string, int> _twPointer = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Timer> _twLiveTimers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _twPartialQuote = new(StringComparer.Ordinal);

    public event Action<short, string>? DomesticStockList;

    public event Action<short, int>? DomesticQuote;

    public event Action<SkDomesticTick>? DomesticTick;

    public event Action<SkBook>? DomesticBest5;

    public event Action<SkLiveBar>? DomesticLiveBar;

    public event Action<SkBook>? OverseasBest5;

    public event Action<SkBook>? OverseasBest10;

    public string GetApiVersion(string loginId)
    {
        _settings.Record("version");
        return _settings.SkcomVersion;
    }

    public int RequestDomesticStockList(short market)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"tw_stocklist {market}"));

        // 手冊 4-4-h:一則含多筆(; 分隔)、多個類股(換行分隔),最後一則以 ## 開頭。Chunks, then a ## chunk.
        string[] chunks = FakeDomesticQuotes.StockListChunks(market);

        _ = Task.Run(async () =>
        {
            await Task.Delay(20).ConfigureAwait(false);

            // 別的市場的一則:宿主必須只收這次要的市場。A chunk of another market, which the host must drop.
            DomesticStockList?.Invoke(market == 2 ? (short)3 : (short)2, "ZZZ,別的市場,0,ZZZ,1,NTD;");

            foreach (string chunk in chunks)
            {
                DomesticStockList?.Invoke(market, chunk);
            }

            // 實測 2026-11-09:結尾是獨立一則「##,,;」。Measured: the end arrives as its own "##,,;" chunk.
            DomesticStockList?.Invoke(market, "##,,;");
        });

        return 0;
    }

    public int RequestDomesticStocks(string codes)
    {
        _settings.Record("tw_stocks " + codes);

        // 手冊 4-4-2:代碼不存在直接略過、不回錯誤。Unknown codes are skipped without an error.
        List<FakeDomesticInstrument> known = [.. codes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(FakeDomesticQuotes.Find).OfType<FakeDomesticInstrument>()];

        foreach (FakeDomesticInstrument instrument in known)
        {
            _twPartialQuote[instrument.Code] = true;
        }

        // 實測 2026-11-09:訂閱後第一則報價只填一部分(其餘 0),約 10 毫秒後下一則補齊。The first quote is partial, the next one full.
        Schedule(TimeSpan.FromMilliseconds(60), () =>
        {
            foreach (FakeDomesticInstrument instrument in known)
            {
                DomesticQuote?.Invoke(instrument.Market, instrument.Index);
            }

            Schedule(TimeSpan.FromMilliseconds(10), () =>
            {
                foreach (FakeDomesticInstrument instrument in known)
                {
                    DomesticQuote?.Invoke(instrument.Market, instrument.Index);
                }
            });
        });

        return 0;
    }

    public int RequestDomesticTicks(short page, string code)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"tw_ticks {page} {code}"));

        if (FakeDomesticQuotes.Find(code) is not { } instrument)
        {
            return CodeTickStockNotFound;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(30).ConfigureAwait(false);

            // 手冊 4-4-l:首次索取先回補當天成交;第一筆是 08:45 的試撮(nSimulate = 1)。Replay first; the first is simulated.
            foreach (SkDomesticTick tick in FakeDomesticQuotes.HistoryTicks(instrument))
            {
                _twPointer.AddOrUpdate(code, tick.Pointer, (_, previous) => Math.Max(previous, tick.Pointer));
                DomesticTick?.Invoke(tick);
            }

            DomesticBest5?.Invoke(FakeBooks.DomesticBook(instrument.Market, instrument.Index, instrument.Close));

            // 手冊 4-4-u:nType 1 先回補當日分 K,之後 nType 2 每筆更新。Replayed minute bars first, then per-tick updates.
            DomesticLiveBar?.Invoke(new SkLiveBar(0, code, 0, 0, 0, 0, 0, 0, 0));
            DomesticLiveBar?.Invoke(new SkLiveBar(1, code, 20261106, 846, instrument.Close, instrument.Close + 200, instrument.Close - 100, instrument.Close + 100, 120));
            DomesticLiveBar?.Invoke(new SkLiveBar(1, code, 20261106, 847, instrument.Close + 100, instrument.Close + 100, instrument.Close, instrument.Close, 80));
            DomesticLiveBar?.Invoke(new SkLiveBar(2, code, 20261106, 848, instrument.Close, instrument.Close, instrument.Close, instrument.Close, 3));

            StartDomesticLiveTicks(instrument);
        });

        return 0;
    }

    public int GetDomesticStockByIndex(short market, int index, out SkDomesticStock stock)
    {
        if (FakeDomesticQuotes.Find(market, index) is not { } instrument)
        {
            stock = default;
            return CodeStockNotFound;
        }

        stock = FakeDomesticQuotes.Stock(instrument);

        // 實測 2026-11-09 小台第一則:只有開高低,成交價、買賣價、單量、總量、成交時間是 0。The measured partial first snapshot.
        if (_twPartialQuote.TryUpdate(instrument.Code, false, true))
        {
            stock = stock with { Close = 0, Bid = 0, Ask = 0, TickQuantity = 0, TotalQuantity = 0, DealTime = 0 };
        }

        return 0;
    }

    public int GetDomesticStockByCode(short market, string code, out SkDomesticStock stock)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"tw_lookup {market} {code}"));

        if (FakeDomesticQuotes.Find(code) is not { } instrument || instrument.Market != market)
        {
            stock = default;
            return CodeStockNotFound;
        }

        stock = FakeDomesticQuotes.Stock(instrument);
        return 0;
    }

    public int RequestMarketDepth(string stockNo)
    {
        _settings.Record("depth " + stockNo);
        FakeInstrument instrument = Resolve(stockNo);

        Schedule(TimeSpan.FromMilliseconds(40), () =>
        {
            OverseasBest10?.Invoke(FakeBooks.Book(instrument.Index, instrument.Quote.Close, 10));
            OverseasBest10?.Invoke(FakeBooks.Book(instrument.Index, instrument.Quote.Close + 1, 10));
        });

        return 0;
    }

    private void StartDomesticLiveTicks(FakeDomesticInstrument instrument)
    {
        if (_settings.LiveTickInterval <= TimeSpan.Zero)
        {
            return;
        }

        _ = _twLiveTimers.GetOrAdd(instrument.Code, _ => new Timer(
            _ =>
            {
                int pointer = _twPointer.AddOrUpdate(instrument.Code, 0, (_, previous) => previous + 1);
                DateTime taipei = DateTime.UtcNow.AddHours(8);
                int date = int.Parse(taipei.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                int time = int.Parse(taipei.ToString("HHmmss", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                long price = instrument.Close + (pointer % 2 == 0 ? 100 : -100);
                DomesticTick?.Invoke(new SkDomesticTick(instrument.Market, instrument.Index, pointer, date, time, 0, price - 100, price, price, 1, 0, false));
                DomesticBest5?.Invoke(FakeBooks.DomesticBook(instrument.Market, instrument.Index, price));
            },
            null,
            _settings.LiveTickInterval,
            _settings.LiveTickInterval));
    }

    private void DisposeLive()
    {
        foreach (Timer timer in _twLiveTimers.Values)
        {
            timer.Dispose();
        }
    }
}

/// <summary>
/// 假檔位(價格每檔差 1 個原始單位,量遞增)。
/// Made-up order books: one raw unit per level, increasing quantities.
/// </summary>
internal static class FakeBooks
{
    public static SkBook Book(int index, long close, int levels)
    {
        long[] bids = [.. Enumerable.Range(0, levels).Select(level => close - 1 - level)];
        long[] asks = [.. Enumerable.Range(0, levels).Select(level => close + 1 + level)];
        long[] quantities = [.. Enumerable.Range(1, levels).Select(level => (long)level)];
        return new SkBook(-1, index, bids, quantities, asks, quantities);
    }

    public static SkBook DomesticBook(short market, int index, long close)
    {
        SkBook book = Book(index, close, 5);

        // 手冊 4-4-n:衍生一檔 + 試算旗標。The extended level and the simulate flag.
        return book with { Market = market, Extended = (close - 600, 7, close + 600, 8), Simulate = 0 };
    }
}

/// <summary>一檔假國內商品。One made-up domestic instrument.</summary>
internal sealed record FakeDomesticInstrument(short Market, int Index, string Code, string Name, int Decimals, long Close, string OrderCode, string LastTradeDay);

/// <summary>
/// 假國內商品目錄:台指 / 小台 / 微台近月(市場 2)與一檔台指選擇權(市場 3)。實測 2026-11-09:期貨與台指選擇權的 <c>sDecimal</c> 都是 2,
/// 近月連續代碼的交易所代碼是 <c>TXFK600</c> 這種、選擇權名稱像「台選11C」。代碼沿用實測格式,價格是假的。
/// The made-up domestic catalog: TAIEX, mini and micro near months (market 2) and one TAIEX option (market 3); both
/// markets use 2 decimals as measured. Prices are made up.
/// </summary>
internal static class FakeDomesticQuotes
{
    public static readonly FakeDomesticInstrument[] Instruments =
    [
        new(2, 0, "TX00", "台指近", 2, 3978600, "TXFK600", "20261118"),
        new(2, 1, "MTX00", "小台近", 2, 3978600, "MXFK600", "20261118"),
        new(2, 2, "TM0000", "微台近", 2, 3978600, "TMFK600", "20261118"),
        new(3, 0, "TXO39800K6", "台選11C", 2, 123500, "TXO39800K6", "20261118"),
    ];

    public static FakeDomesticInstrument? Find(string code) => Array.Find(Instruments, item => item.Code == code);

    public static FakeDomesticInstrument? Find(short market, int index) => Array.Find(Instruments, item => item.Market == market && item.Index == index);

    /// <summary>
    /// 手冊 4-4-h 格式:<c>[代碼],[名稱],[最後交易日],[交易所商品代碼],[跳動點],[幣別];</c>,類股之間換行。實測 2026-11-09:每檔先一筆結尾
    /// 多「AM」的日盤代碼、再一筆全盤代碼(交易所代碼相同),跳動點像 <c>100000| 1 | </c>;選擇權清單另有 5 欄的系列列(<c>TXO11,台選11,0,TXO11,</c>)。
    /// The format, as measured: each code comes as a day-only "AM" entry and a full-session entry; option lists also carry
    /// five-field series rows.
    /// </summary>
    public static string[] StockListChunks(short market)
    {
        FakeDomesticInstrument[] items = [.. Instruments.Where(item => item.Market == market)];

        if (items.Length == 0)
        {
            return [];
        }

        string Entry(FakeDomesticInstrument item, string suffix) =>
            $"{item.Code}{suffix},{item.Name},{item.LastTradeDay},{item.OrderCode},{(item.Market == 2 ? "100000| 1 | " : "10| 0.1 | / 50| 0.5 | / 500| 1.0 | / 1000| 5.0 | / 100000| 10 | ")},NTD;";
        string Pair(FakeDomesticInstrument item) => Entry(item, "AM") + Entry(item, string.Empty);

        // 第一則兩檔 + 換行 + 其餘;第二則是最後交易日 0 的系列列(選擇權)或不可交易的一筆(期貨)。
        // Two chunks; the second holds a row with a 0 last trading day.
        string first = string.Concat(items.Take(2).Select(Pair)) + "\n" + string.Concat(items.Skip(2).Select(Pair));
        return [first, market == 3 ? "TXO11,台選11,0,TXO11,;" : "TXZZ,不可交易,0,,100000| 1 | ,NTD;"];
    }

    public static SkDomesticStock Stock(FakeDomesticInstrument item) => new()
    {
        Index = item.Index,
        Decimals = item.Decimals,
        TypeNo = 0,
        MarketNo = item.Market.ToString(CultureInfo.InvariantCulture),
        StockNo = item.Code,
        StockName = item.Name,
        StockNoSpread = string.Empty,
        Open = item.Close - 1000,
        High = item.Close + 2000,
        Low = item.Close - 3000,
        Close = item.Close,
        TickQuantity = 2,
        Reference = item.Close - 500,
        Bid = item.Close - 100,
        BidQuantity = 5,
        Ask = item.Close,
        AskQuantity = 6,
        BuyQuantity = 1000,
        SellQuantity = 900,
        OpenInterest = 80000,
        TotalQuantity = 1900,
        YesterdayQuantity = 2100,
        LimitUp = item.Close + 400000,
        LimitDown = item.Close - 400000,
        Simulate = 0,
        TradingDay = 20261106,
        DealTime = 134500,
    };

    /// <summary>
    /// 回補三筆:08:44:55 試撮、08:45:00.123、08:46:01。實測 2026-11-09:回補從開盤前的試撮開始(<c>nSimulate</c> = 1,毫秒為 0),
    /// 期貨的 <c>nTimemillismicros</c> 只有毫秒(後三位 000)。Three replayed ticks; the first is simulated, milliseconds only.
    /// </summary>
    public static IEnumerable<SkDomesticTick> HistoryTicks(FakeDomesticInstrument item)
    {
        yield return new SkDomesticTick(item.Market, item.Index, 0, 20261106, 84455, 0, item.Close - 100, item.Close, item.Close, 3, 1, true);
        yield return new SkDomesticTick(item.Market, item.Index, 1, 20261106, 84500, 123000, item.Close - 100, item.Close, item.Close, 12, 0, true);
        yield return new SkDomesticTick(item.Market, item.Index, 2, 20261106, 84601, 0, item.Close, item.Close + 100, item.Close + 100, 3, 0, true);
    }
}
