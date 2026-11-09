namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// 國內商品基本資料與報價(<c>SKSTOCKLONG</c> 的全部欄位,手冊 5-21;價格是原始整數,÷ 10^<see cref="Decimals"/> 才是價格)。
/// The domestic stock object (every field of SKSTOCKLONG); prices are raw integers to divide by 10^Decimals.
/// </summary>
internal readonly record struct SkDomesticStock
{
    public int Index { get; init; }

    public int Decimals { get; init; }

    public int TypeNo { get; init; }

    public string MarketNo { get; init; }

    public string StockNo { get; init; }

    public string StockName { get; init; }

    public string StockNoSpread { get; init; }

    public long Open { get; init; }

    public long High { get; init; }

    public long Low { get; init; }

    public long Close { get; init; }

    public long TickQuantity { get; init; }

    public long Reference { get; init; }

    public long Bid { get; init; }

    public long BidQuantity { get; init; }

    public long Ask { get; init; }

    public long AskQuantity { get; init; }

    public long BuyQuantity { get; init; }

    public long SellQuantity { get; init; }

    public long OpenInterest { get; init; }

    public long TotalQuantity { get; init; }

    public long YesterdayQuantity { get; init; }

    public long LimitUp { get; init; }

    public long LimitDown { get; init; }

    public int Simulate { get; init; }

    public int DayTrade { get; init; }

    public int TradingDay { get; init; }

    public int TradingLotFlag { get; init; }

    public int DealTime { get; init; }

    public long OddLotBid { get; init; }

    public long OddLotAsk { get; init; }

    public long OddLotClose { get; init; }

    public long OddLotQuantity { get; init; }
}

/// <summary>
/// 國內成交一筆(<c>OnNotifyTicksLONG</c> / <c>OnNotifyHistoryTicksLONG</c>,手冊 4-4-l、4-4-m;原樣不解析)。
/// One domestic tick, unparsed.
/// </summary>
/// <param name="Market">市場別(sMarketNo)。The market number.</param>
/// <param name="Index">索引。The stock index.</param>
/// <param name="Pointer">nPtr。</param>
/// <param name="Date">nDate(yyyyMMdd)。</param>
/// <param name="TimeHms">nTimehms(HHmmss)。</param>
/// <param name="TimeMicros">nTimemillismicros(前三位毫秒、後三位微秒)。Milliseconds then microseconds.</param>
/// <param name="Bid">買價(原始整數)。The raw bid.</param>
/// <param name="Ask">賣價(原始整數)。The raw ask.</param>
/// <param name="Close">成交價(原始整數)。The raw price.</param>
/// <param name="Quantity">成交量。The quantity.</param>
/// <param name="Simulate">0 一般揭示、1 試算揭示。0 regular, 1 simulated.</param>
/// <param name="IsHistory">訂閱當下的回補。Replayed at subscription.</param>
internal readonly record struct SkDomesticTick(
    short Market,
    int Index,
    int Pointer,
    int Date,
    int TimeHms,
    int TimeMicros,
    long Bid,
    long Ask,
    long Close,
    long Quantity,
    int Simulate,
    bool IsHistory);

/// <summary>
/// 委買委賣檔位一則(國內五檔、海外五檔 / 十檔、海外選擇權五檔 / 十檔;價格是原始整數)。
/// One order book notification; prices are raw integers.
/// </summary>
/// <param name="Market">國內的市場別;海外沒有,固定 -1。The domestic market number; -1 overseas.</param>
/// <param name="Index">索引。The stock index.</param>
/// <param name="BidPrices">買價(第一檔起)。Bid prices from level 1.</param>
/// <param name="BidQuantities">買量。Bid quantities.</param>
/// <param name="AskPrices">賣價。Ask prices.</param>
/// <param name="AskQuantities">賣量。Ask quantities.</param>
/// <param name="Extended">國內五檔的衍生一檔(買價, 買量, 賣價, 賣量);海外沒有。The domestic extended level; none overseas.</param>
/// <param name="Simulate">國內的試算旗標;海外沒有。The domestic simulate flag; none overseas.</param>
internal sealed record SkBook(
    short Market,
    int Index,
    IReadOnlyList<long> BidPrices,
    IReadOnlyList<long> BidQuantities,
    IReadOnlyList<long> AskPrices,
    IReadOnlyList<long> AskQuantities,
    (long Bid, long BidQuantity, long Ask, long AskQuantity)? Extended = null,
    int? Simulate = null);

/// <summary>
/// 國內即時分 K 一則(<c>OnNotifyLiveKLineData</c>,手冊 4-4-u;價格是原始整數)。
/// One domestic live K-line notification; prices are raw integers.
/// </summary>
/// <param name="Type">0 資料重置(價格都是 0)、1 即時分 K(含當日回補)、2 該分鐘每筆更新。0 reset, 1 minute bar, 2 per tick.</param>
/// <param name="StockNo">商品代碼。The code.</param>
/// <param name="Date">yyyyMMdd。</param>
/// <param name="TimeHm">HHmm(例如 904 = 09:04)。HHmm, for example 904.</param>
/// <param name="Open">開。Open.</param>
/// <param name="High">高。High.</param>
/// <param name="Low">低。Low.</param>
/// <param name="Close">收(目前價)。Close.</param>
/// <param name="Quantity">量。Volume.</param>
internal readonly record struct SkLiveBar(int Type, string StockNo, int Date, int TimeHm, long Open, long High, long Low, long Close, long Quantity);

/// <summary>
/// 海外選擇權報價(<c>SKFOREIGNLONG</c>;價格是原始整數)。
/// One overseas option quote (SKFOREIGNLONG); prices are raw integers.
/// </summary>
internal readonly record struct SkOptionQuote
{
    public string MarketNo { get; init; }

    public string ExchangeNo { get; init; }

    public string ExchangeName { get; init; }

    public string StockNo { get; init; }

    public string StockName { get; init; }

    public string CallPut { get; init; }

    public int Decimals { get; init; }

    public int Denominator { get; init; }

    public long Open { get; init; }

    public long High { get; init; }

    public long Low { get; init; }

    public long Close { get; init; }

    public long Settle { get; init; }

    public long TickQuantity { get; init; }

    public long Reference { get; init; }

    public long Bid { get; init; }

    public long BidQuantity { get; init; }

    public long Ask { get; init; }

    public long AskQuantity { get; init; }

    public long TotalQuantity { get; init; }

    public long StrikePrice { get; init; }

    public int TradingDay { get; init; }
}
