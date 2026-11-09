using Ozakboy.TradeKit.Capital.Host.Serve;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

// 0.3.0:SKCOM 版本、國內即時報價與海外十檔的 COM 實作。只用 SkCom*Interop.cs 既有的真簽章(產生器規格 skcom-interop.psd1),沒有新增任何簽章。
// 0.3.0: the COM side of the version, domestic live quotes and the overseas best 10, using only the existing signatures.
internal sealed partial class SkComQuoteSource
{
    /// <inheritdoc/>
    public event Action<short, string>? DomesticStockList;

    /// <inheritdoc/>
    public event Action<short, int>? DomesticQuote;

    /// <inheritdoc/>
    public event Action<SkDomesticTick>? DomesticTick;

    /// <inheritdoc/>
    public event Action<SkBook>? DomesticBest5;

    /// <inheritdoc/>
    public event Action<SkLiveBar>? DomesticLiveBar;

    /// <inheritdoc/>
    public event Action<SkBook>? OverseasBest5;

    /// <inheritdoc/>
    public event Action<SkBook>? OverseasBest10;

    /// <inheritdoc/>
    public string GetApiVersion(string loginId) => Center.SKCenterLib_GetSKAPIVersionAndBit(loginId) ?? string.Empty;

    /// <inheritdoc/>
    public int RequestDomesticStockList(short market) => Domestic.SKQuoteLib_RequestStockList(market);

    /// <inheritdoc/>
    public int RequestDomesticStocks(string codes)
    {
        // 手冊 4-4-2:psPageNo 自 V2.13.54 起「請固定帶 1」。The manual says to always pass 1 since V2.13.54.
        short page = 1;
        return Domestic.SKQuoteLib_RequestStocks(ref page, codes);
    }

    /// <inheritdoc/>
    public int RequestDomesticTicks(short page, string code)
    {
        short pageRef = page;
        return Domestic.SKQuoteLib_RequestTicks(ref pageRef, code);
    }

    /// <inheritdoc/>
    public int GetDomesticStockByIndex(short market, int index, out SkDomesticStock stock)
    {
        SKSTOCKLONG item = default;
        int code = Domestic.SKQuoteLib_GetStockByIndexLONG(market, index, ref item);
        stock = ToStock(item);
        return code;
    }

    /// <inheritdoc/>
    public int GetDomesticStockByCode(short market, string code, out SkDomesticStock stock)
    {
        SKSTOCKLONG item = default;
        int result = Domestic.SKQuoteLib_GetStockByMarketAndNo(market, code, ref item);
        stock = ToStock(item);
        return result;
    }

    /// <inheritdoc/>
    public int RequestMarketDepth(string stockNo)
    {
        short page = -1;
        return OsQuote.SKOSQuoteLib_RequestMarketDepth(ref page, stockNo);
    }

    /// <summary>
    /// 型別庫參數順序的檔位值 → <see cref="SkBook"/>:每檔(價, 量)交錯;國內五檔在買、賣第五檔後各有衍生一檔,最後是試算旗標。
    /// Converts the type library's parameter order into a book: price and quantity alternate per level; the domestic
    /// best 5 has an extended level after each side's fifth level and the simulate flag last.
    /// </summary>
    internal static SkBook ToBook(SkBookEvent book, int levels, bool extended)
    {
        IReadOnlyList<long> values = book.Values;
        int side = (levels * 2) + (extended ? 2 : 0);
        long[] bidPrices = new long[levels];
        long[] bidQuantities = new long[levels];
        long[] askPrices = new long[levels];
        long[] askQuantities = new long[levels];

        for (int level = 0; level < levels; level++)
        {
            bidPrices[level] = values[level * 2];
            bidQuantities[level] = values[(level * 2) + 1];
            askPrices[level] = values[side + (level * 2)];
            askQuantities[level] = values[side + (level * 2) + 1];
        }

        if (!extended)
        {
            return new SkBook(book.MarketNo, book.StockIdx, bidPrices, bidQuantities, askPrices, askQuantities);
        }

        int bidExtended = levels * 2;
        int askExtended = side + (levels * 2);

        return new SkBook(
            book.MarketNo,
            book.StockIdx,
            bidPrices,
            bidQuantities,
            askPrices,
            askQuantities,
            (values[bidExtended], values[bidExtended + 1], values[askExtended], values[askExtended + 1]),
            (int)values[side * 2]);
    }

    private static SkDomesticStock ToStock(SKSTOCKLONG item) => new()
    {
        Index = item.nStockIdx,
        Decimals = item.sDecimal,
        TypeNo = item.sTypeNo,
        MarketNo = item.bstrMarketNo ?? string.Empty,
        StockNo = item.bstrStockNo ?? string.Empty,
        StockName = item.bstrStockName ?? string.Empty,
        StockNoSpread = item.bstrStockNoSpread ?? string.Empty,
        Open = item.nOpen,
        High = item.nHigh,
        Low = item.nLow,
        Close = item.nClose,
        TickQuantity = item.nTickQty,
        Reference = item.nRef,
        Bid = item.nBid,
        BidQuantity = item.nBc,
        Ask = item.nAsk,
        AskQuantity = item.nAc,
        BuyQuantity = item.nTBc,
        SellQuantity = item.nTAc,
        OpenInterest = item.nFutureOI,
        TotalQuantity = item.nTQty,
        YesterdayQuantity = item.nYQty,
        LimitUp = item.nUp,
        LimitDown = item.nDown,
        Simulate = item.nSimulate,
        DayTrade = item.nDayTrade,
        TradingDay = item.nTradingDay,
        TradingLotFlag = item.nTradingLotFlag,
        DealTime = item.nDealTime,
        OddLotBid = item.nOddLotBid,
        OddLotAsk = item.nOddLotAsk,
        OddLotClose = item.nOddLotClose,
        OddLotQuantity = item.nOddLotQty,
    };

    private void HookDomesticLiveEvents(QuoteEventsSink sink)
    {
        sink.StockList += (market, data) => DomesticStockList?.Invoke(market, data);
        sink.Quote += (market, index) => DomesticQuote?.Invoke(market, index);
        sink.Tick += tick => DomesticTick?.Invoke(new SkDomesticTick(
            tick.MarketNo,
            tick.StockIdx,
            tick.Ptr,
            tick.Date,
            tick.TimeHms,
            tick.TimeMillisMicros,
            tick.Bid,
            tick.Ask,
            tick.Close,
            tick.Qty,
            tick.Simulate,
            tick.History));
        sink.Best5 += book => DomesticBest5?.Invoke(ToBook(book, 5, extended: true));
        sink.LiveKLine += bar => DomesticLiveBar?.Invoke(new SkLiveBar(bar.Type, bar.StockNo, bar.Date, bar.TimeHm, bar.Open, bar.High, bar.Low, bar.Close, bar.Qty));
    }
}
