namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆國內成交(<c>OnNotifyTicksLONG</c> / <c>OnNotifyHistoryTicksLONG</c>,手冊 4-4-l、4-4-m)。價格 = 原始整數 ÷ 10^小數位數。
/// <see cref="TimeUtc"/> = <c>nDate</c>(yyyyMMdd)+ <c>nTimehms</c>(HHmmss)+ <c>nTimemillismicros</c>(前三位毫秒、後三位微秒),
/// 當作台北時間(UTC+8)轉 UTC。
/// <para>
/// <b>實測 2026-11-09</b>(夜盤,台指 / 小台 / 微台近月共 16.8 萬筆):<c>nDate</c> 是<b>日曆日</b>(夜盤 20:11 的成交是 20261109,
/// 不是交易日 20261110);即時成交 20:11:07.817 在 UTC 12:11:07.989 收到,轉換正確;期貨的 <c>nTimemillismicros</c> 有值但後三位一律 000
/// (只有毫秒精度,例如 817000 = 817 毫秒)。夜盤跨午夜後的 <c>nDate</c> 未實測(推定是新的日曆日)。
/// 訂閱當下先回補當天(夜盤是從 14:50:05 的試撮開始)的成交再接即時;14:50–15:00 的試撮列 <see cref="IsSimulated"/> = true,15:00:00 起為 false。
/// </para>
/// One domestic tick. Measured on 2026-11-09: nDate is the calendar day (not the trading day), the Taipei-to-UTC
/// conversion matches the receive time, and futures carry milliseconds only (the last three digits are always 000).
/// After midnight is not measured yet. The replay starts with the pre-open simulated matching, flagged IsSimulated.
/// </summary>
public sealed record CapitalDomesticTick : CapitalMarketEvent
{
    /// <summary>群益商品代碼。Capital's code.</summary>
    public required string Code { get; init; }

    /// <summary>市場別。The market.</summary>
    public required CapitalDomesticMarket Market { get; init; }

    /// <summary>序號(<c>nPtr</c>),每檔從 0 遞增,可用來去重。The sequence, increasing from 0 per code; use it to de-duplicate.</summary>
    public required long Sequence { get; init; }

    /// <summary>成交時間(UTC;見型別說明)。The tick time in UTC; see the type remarks.</summary>
    public required DateTimeOffset TimeUtc { get; init; }

    /// <summary>群益的 <c>nDate</c> 原值(yyyyMMdd,日曆日)。Capital's raw nDate, a calendar day.</summary>
    public required int Date { get; init; }

    /// <summary>群益的 <c>nTimehms</c> 原值(HHmmss)。Capital's raw nTimehms.</summary>
    public required int Time { get; init; }

    /// <summary>
    /// 群益的 <c>nTimemillismicros</c> 原值(前三位毫秒、後三位微秒;實測期貨後三位一律 0)。
    /// Capital's raw nTimemillismicros; futures carry milliseconds only, as measured.
    /// </summary>
    public int Micros { get; init; }

    /// <summary>成交價;0 → null(實測試撮回補列可能整筆是 0)。Price; null for zero (a simulated replay row may be all zero).</summary>
    public decimal? Price { get; init; }

    /// <summary>買價;0(沒有買價)→ null。Bid; null for zero.</summary>
    public decimal? Bid { get; init; }

    /// <summary>賣價;0(沒有賣價)→ null。Ask; null for zero.</summary>
    public decimal? Ask { get; init; }

    /// <summary>成交量(口)。Quantity in contracts.</summary>
    public required long Quantity { get; init; }

    /// <summary>
    /// 是否為試算揭示(試撮,<c>nSimulate</c> = 1);開盤前的試撮不是真的成交(實測夜盤 14:50:05–14:59:55 為 1)。
    /// Whether it is a simulated display; measured 1 for the pre-open matching before 15:00.
    /// </summary>
    public required bool IsSimulated { get; init; }

    /// <summary>是否為訂閱當下回補的當日成交。Whether it is part of the replay at subscription.</summary>
    public required bool IsReplay { get; init; }
}
