namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一則國內即時分 K(<c>OnNotifyLiveKLineData</c>,手冊 4-4-u)。價格 = 原始整數 ÷ 10^小數位數;小數位數不知道(宿主還沒查到這個代碼)或
/// <see cref="CapitalLiveBarKind.Reset"/> 時價格為 <see langword="null"/>。
/// <para>
/// <b>實測 2026-11-09</b>(夜盤):<see cref="Date"/> + <see cref="TimeHm"/> 是那一分鐘的<b>結束</b>時刻(與歷史分 K 相同):夜盤第一根標 1501,
/// 開盤價等於 15:00:00.003 第一筆成交;20:11:07 收到的即時更新標 2012。所以 <see cref="OpenTimeUtc"/> = 台北時間的結束時刻 − 1 分鐘轉 UTC,
/// 與 <see cref="CapitalDomesticBar.OpenTimeUtc"/> 一致。<see cref="Date"/> 實測是日曆日(20261109,不是交易日);跨午夜那根(0000)的日期
/// 未實測(歷史分 K 是新的一天)。訂閱時先以 <see cref="CapitalLiveBarKind.Minute"/> 回補當日(夜盤從 1501 起)已完成的分 K,之後
/// <see cref="CapitalLiveBarKind.Update"/> 是「還在形成的那一分鐘」每筆成交後的累計開高低收量,分鐘結束後再來一則 <see cref="CapitalLiveBarKind.Minute"/>。
/// <see cref="CapitalLiveBarKind.Reset"/> 在實測期間沒有出現。
/// </para>
/// One domestic live bar. Measured on 2026-11-09: the date and HHmm are the end of the minute, as for historical bars,
/// so OpenTimeUtc is the Taipei end time minus one minute in UTC. Minute rows replay the finished bars first; Update rows
/// then carry the forming minute's running values. No reset was seen.
/// </summary>
public sealed record CapitalDomesticLiveBar : CapitalMarketEvent
{
    /// <summary>群益商品代碼。Capital's code.</summary>
    public required string Code { get; init; }

    /// <summary>種類。The kind.</summary>
    public required CapitalLiveBarKind Kind { get; init; }

    /// <summary>群益的 nDate 原值(yyyyMMdd,日曆日;重置時為 0)。The raw nDate, a calendar day; 0 on a reset.</summary>
    public required int Date { get; init; }

    /// <summary>群益的 nTimehm 原值(HHmm,那一分鐘的結束時刻,例如 1501 = 15:00–15:01 那根)。The raw HHmm, the end of the minute.</summary>
    public required int TimeHm { get; init; }

    /// <summary>
    /// 這根分 K 的開始時刻(UTC)= <see cref="Date"/> + <see cref="TimeHm"/>(結束時刻,台北時間)− 1 分鐘;讀不懂(含重置)時為 null。
    /// The bar's open time in UTC, the Taipei end time minus one minute; null when unreadable, a reset included.
    /// </summary>
    public DateTimeOffset? OpenTimeUtc { get; init; }

    /// <summary>開盤價。Open.</summary>
    public decimal? Open { get; init; }

    /// <summary>最高價。High.</summary>
    public decimal? High { get; init; }

    /// <summary>最低價。Low.</summary>
    public decimal? Low { get; init; }

    /// <summary>收盤價(目前價)。Close, the current price.</summary>
    public decimal? Close { get; init; }

    /// <summary>量(<see cref="CapitalLiveBarKind.Update"/> 是這一分鐘到目前的累計);重置時為 null。Volume, running for Update rows; null on a reset.</summary>
    public long? Volume { get; init; }
}
