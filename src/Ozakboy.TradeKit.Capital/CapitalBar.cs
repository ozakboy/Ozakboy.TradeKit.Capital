namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一根歷史 K 線。分 K 有 <see cref="OpenTimeUtc"/>(群益給的是那一分鐘的結束時刻,這裡已減 1 分鐘成開始時刻);
/// 日 / 週 / 月 K 只有 <see cref="TradingDay"/>。
/// One historical bar. A minute bar has <see cref="OpenTimeUtc"/> (Capital gives the end of the minute; one minute is
/// already subtracted here); day, week and month bars have only <see cref="TradingDay"/>.
/// </summary>
public sealed record CapitalBar
{
    /// <summary>商品。The symbol.</summary>
    public required CapitalSymbol Symbol { get; init; }

    /// <summary>K 線類型。The bar type.</summary>
    public required CapitalKLineType Type { get; init; }

    /// <summary>分 K 的開始時刻(UTC);其他類型為 <see langword="null"/>。A minute bar's open time, UTC; null otherwise.</summary>
    public DateTimeOffset? OpenTimeUtc { get; init; }

    /// <summary>日 / 週 / 月 K 的日期;分 K 為 <see langword="null"/>。The date of a day, week or month bar; null for minutes.</summary>
    public DateOnly? TradingDay { get; init; }

    /// <summary>開盤價。Open.</summary>
    public required decimal Open { get; init; }

    /// <summary>最高價。High.</summary>
    public required decimal High { get; init; }

    /// <summary>最低價。Low.</summary>
    public required decimal Low { get; init; }

    /// <summary>收盤價。Close.</summary>
    public required decimal Close { get; init; }

    /// <summary>量(外匯是報價次數)。Volume; a quote count for FX.</summary>
    public required long Volume { get; init; }
}
