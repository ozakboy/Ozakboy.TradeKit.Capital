namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆海外選擇權成交(SKOOQuoteLib 的 <c>OnNotifyTicksLONG</c> / <c>OnNotifyHistoryTicksLONG</c>)。時間依交易所時區轉 UTC
/// (<see cref="CapitalExchangeClock"/>);交易所沒有已知時區時 <see cref="TimeUtc"/> 為 null,原始日期時間在 <see cref="Date"/> /
/// <see cref="Time"/>。<b>時間是否為交易所當地時間待實測</b>(海期實測是,這裡比照;2026-11-09 實測挑的兩檔三分鐘內沒有成交)。
/// 商品清單裡有 Eurex、HKEx、ICEEU、OSE 等沒有實測時區的交易所。
/// One overseas option tick. Its time is converted by the exchange's zone; without a known zone TimeUtc is null and the
/// raw date and time are kept. <b>Whether the time is exchange-local is not measured yet</b>; it is assumed like the
/// overseas futures.
/// </summary>
public sealed record CapitalOverseasOptionTick : CapitalMarketEvent
{
    /// <summary>序號(<c>nPtr</c>)。The sequence.</summary>
    public required long Sequence { get; init; }

    /// <summary>成交時間(UTC);交易所時區不明時為 null。The tick time in UTC; null for an unknown exchange.</summary>
    public DateTimeOffset? TimeUtc { get; init; }

    /// <summary>
    /// 時區是否實測過(<see cref="CapitalExchangeTimeZone.IsVerified"/>);<see cref="TimeUtc"/> 為 null 時為 false。
    /// Whether the zone was measured; false when TimeUtc is null.
    /// </summary>
    public required bool IsTimeZoneVerified { get; init; }

    /// <summary>群益的日期原文(yyyyMMdd)。Capital's raw date.</summary>
    public required string Date { get; init; }

    /// <summary>群益的時間原文(HHmmss)。Capital's raw time.</summary>
    public required string Time { get; init; }

    /// <summary>成交價。Price.</summary>
    public required decimal Price { get; init; }

    /// <summary>成交量。Quantity.</summary>
    public required long Quantity { get; init; }

    /// <summary>是否為訂閱當下回補的成交。Whether it is part of the replay at subscription.</summary>
    public required bool IsReplay { get; init; }
}
