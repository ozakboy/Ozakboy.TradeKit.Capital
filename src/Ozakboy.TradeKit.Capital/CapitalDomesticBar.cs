namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一根國內期貨歷史 K 線(<c>SKQuoteLib_RequestKLineAMByDate</c>)。分 K 有 <see cref="OpenTimeUtc"/>,日 K 只有
/// <see cref="TradingDay"/>。資料照群益原樣:量為 0 的空 K 棒(沒有成交的分鐘)不過濾,收盤價可能是結算價(見
/// <see cref="CapitalTradeSession"/>)。
/// One domestic futures historical bar. Minute bars have <see cref="OpenTimeUtc"/>, daily bars only
/// <see cref="TradingDay"/>. The data is Capital's as is: empty bars with zero volume (minutes without trades) are kept,
/// and a close may be the settlement price (see <see cref="CapitalTradeSession"/>).
/// </summary>
public sealed record CapitalDomesticBar
{
    /// <summary>群益商品代碼(例如 <c>TX00</c> 台指近月)。Capital's code, for example TX00.</summary>
    public required string Code { get; init; }

    /// <summary>K 線類型。The bar type.</summary>
    public required CapitalDomesticKLineType Type { get; init; }

    /// <summary>
    /// 分 K 的開始時刻(UTC)= 群益的結束時刻(台北時間,UTC+8,台灣無夏令)− 1 分鐘;日 K 為 <see langword="null"/>。
    /// 群益 00:00 那根的日期已是新的一天,所以它的開始時刻是前一天 23:59(台北)。
    /// A minute bar's open time in UTC: Capital's end time (Taipei, UTC+8, no daylight saving) minus one minute; null for
    /// daily bars. Capital dates the 00:00 bar on the new day, so it opens at 23:59 Taipei time of the day before.
    /// </summary>
    public DateTimeOffset? OpenTimeUtc { get; init; }

    /// <summary>日 K 的交易日;分 K 為 <see langword="null"/>。The trading day of a daily bar; null for minute bars.</summary>
    public DateOnly? TradingDay { get; init; }

    /// <summary>開盤價。Open.</summary>
    public required decimal Open { get; init; }

    /// <summary>最高價。High.</summary>
    public required decimal High { get; init; }

    /// <summary>最低價。Low.</summary>
    public required decimal Low { get; init; }

    /// <summary>收盤價(全盤的日盤最後一根與全盤日 K 實測是結算價)。Close; the settlement price in the cases measured.</summary>
    public required decimal Close { get; init; }

    /// <summary>量(口);空 K 棒為 0。Volume in contracts; zero for an empty bar.</summary>
    public required long Volume { get; init; }
}
