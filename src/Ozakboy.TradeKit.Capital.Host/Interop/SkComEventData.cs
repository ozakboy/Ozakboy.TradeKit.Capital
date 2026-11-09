namespace Ozakboy.TradeKit.Capital.Host.Interop;

/// <summary>
/// 國內成交一筆(<c>OnNotifyTicksLONG</c> / <c>OnNotifyHistoryTicksLONG</c> 的全部欄位,原樣不解析)。
/// One domestic tick with every field of OnNotifyTicksLONG / OnNotifyHistoryTicksLONG, unparsed.
/// </summary>
/// <param name="MarketNo">市場別。Market number.</param>
/// <param name="StockIdx">索引。Stock index.</param>
/// <param name="Ptr">序號。Sequence.</param>
/// <param name="Date">日期 yyyyMMdd。Date.</param>
/// <param name="TimeHms">時間 HHmmss。Time.</param>
/// <param name="TimeMillisMicros">毫秒微秒。Milliseconds and microseconds.</param>
/// <param name="Bid">買價。Bid.</param>
/// <param name="Ask">賣價。Ask.</param>
/// <param name="Close">成交價。Price.</param>
/// <param name="Qty">成交量。Quantity.</param>
/// <param name="Simulate">0 一般揭示、1 試算揭示。0 regular, 1 simulated.</param>
/// <param name="History">是否為訂閱當下的回補。Whether it is the replay at subscription.</param>
internal readonly record struct SkDomesticTickEvent(
    short MarketNo,
    int StockIdx,
    int Ptr,
    int Date,
    int TimeHms,
    int TimeMillisMicros,
    int Bid,
    int Ask,
    int Close,
    int Qty,
    int Simulate,
    bool History);

/// <summary>
/// 海外選擇權成交一筆(<c>OnNotifyTicksLONG</c> / <c>OnNotifyHistoryTicksLONG</c>,原樣不解析)。
/// One overseas option tick, unparsed.
/// </summary>
/// <param name="StockIdx">索引。Stock index.</param>
/// <param name="Ptr">序號。Sequence.</param>
/// <param name="Date">日期。Date.</param>
/// <param name="Time">時間。Time.</param>
/// <param name="Close">成交價。Price.</param>
/// <param name="Qty">成交量。Quantity.</param>
/// <param name="History">是否為訂閱當下的回補。Whether it is the replay at subscription.</param>
internal readonly record struct SkForeignTickEvent(int StockIdx, int Ptr, int Date, int Time, int Close, int Qty, bool History);

/// <summary>
/// 國內即時分 K 一則(<c>OnNotifyLiveKLineData</c>,原樣不解析)。
/// One domestic live K-line notification, unparsed.
/// </summary>
/// <param name="Type">0 清盤、1 分 K(含當日回補)、2 該分鐘每筆。0 reset, 1 minute bar, 2 per tick.</param>
/// <param name="StockNo">商品代碼。Symbol.</param>
/// <param name="Date">日期。Date.</param>
/// <param name="TimeHm">時間 HHmm。Time.</param>
/// <param name="Open">開。Open.</param>
/// <param name="High">高。High.</param>
/// <param name="Low">低。Low.</param>
/// <param name="Close">收。Close.</param>
/// <param name="Qty">量。Volume.</param>
internal readonly record struct SkLiveKLineEvent(int Type, string StockNo, int Date, int TimeHm, int Open, int High, int Low, int Close, int Qty);

/// <summary>
/// 委買委賣檔位一則(五檔 / 十檔)。<see cref="Values"/> 照型別庫的參數順序(市場別與索引之後的全部參數),欄位名稱見
/// <see cref="SkBookFields"/>。
/// One order book notification (best 5 or best 10). Values follow the type library's parameter order after the market
/// and index; the field names are in <see cref="SkBookFields"/>.
/// </summary>
/// <param name="MarketNo">市場別(海外沒有,固定 -1)。Market number; -1 for overseas.</param>
/// <param name="StockIdx">索引。Stock index.</param>
/// <param name="Values">各欄位值。Field values.</param>
internal sealed record SkBookEvent(short MarketNo, int StockIdx, IReadOnlyList<long> Values);

/// <summary>
/// <see cref="SkBookEvent.Values"/> 各欄位的名稱(型別庫的參數名)。
/// Field names of <see cref="SkBookEvent.Values"/>, taken from the type library's parameter names.
/// </summary>
internal static class SkBookFields
{
    /// <summary>國內五檔(含延伸買賣與試算旗標)。Domestic best 5 with the extended levels and the simulate flag.</summary>
    public static readonly IReadOnlyList<string> DomesticBest5 =
    [
        .. Levels("nBestBid", "nBestBidQty", 5), "nExtendBid", "nExtendBidQty",
        .. Levels("nBestAsk", "nBestAskQty", 5), "nExtendAsk", "nExtendAskQty", "nSimulate",
    ];

    /// <summary>海外五檔。Overseas best 5.</summary>
    public static readonly IReadOnlyList<string> Best5 = [.. Levels("nBestBid", "nBestBidQty", 5), .. Levels("nBestAsk", "nBestAskQty", 5)];

    /// <summary>海外十檔。Overseas best 10.</summary>
    public static readonly IReadOnlyList<string> Best10 = [.. Levels("nBestBid", "nBestBidQty", 10), .. Levels("nBestAsk", "nBestAskQty", 10)];

    private static IEnumerable<string> Levels(string price, string quantity, int count)
    {
        for (int level = 1; level <= count; level++)
        {
            yield return price + level.ToString(System.Globalization.CultureInfo.InvariantCulture);
            yield return quantity + level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
