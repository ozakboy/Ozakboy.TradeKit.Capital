namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆成交。時間由交易所當地時間轉成 UTC(見 <see cref="CapitalExchangeClock"/>)。
/// One tick. Its time is converted from the exchange's local time to UTC (see <see cref="CapitalExchangeClock"/>).
/// </summary>
public sealed record CapitalTick : CapitalMarketEvent
{
    /// <summary>
    /// 序號(<c>nPtr</c>),每檔遞增,可用來去重(訂閱當下的回補與重啟後的回補會重複送)。
    /// The sequence (nPtr), increasing per symbol; use it to de-duplicate, since replays repeat after a restart.
    /// </summary>
    public required long Sequence { get; init; }

    /// <summary>成交時間(UTC,秒級)。The tick time, UTC, to the second.</summary>
    public required DateTimeOffset TimeUtc { get; init; }

    /// <summary>成交價。Price.</summary>
    public required decimal Price { get; init; }

    /// <summary>成交量(外匯實測多為 1)。Quantity; mostly 1 for FX as measured.</summary>
    public required long Quantity { get; init; }

    /// <summary>
    /// 是否為訂閱當下回補的當個交易日成交(<c>OnNotifyHistoryTicksNineDigitLONG</c>)。
    /// Whether it is part of the trading day's replay sent at subscription.
    /// </summary>
    public required bool IsReplay { get; init; }
}
