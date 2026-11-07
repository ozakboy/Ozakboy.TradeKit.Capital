namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 歷史 K 線類型(<c>RequestKLineByDate</c> 的 sKLineType;實測 4–7 回 1042 不支援)。
/// The historical K-line type (sKLineType of RequestKLineByDate; 4 to 7 were measured to answer 1042, unsupported).
/// </summary>
public enum CapitalKLineType
{
    /// <summary>1 分 K;時間是那一分鐘的結束。One-minute bars; the time is the end of the minute.</summary>
    Minute = 0,

    /// <summary>日 K(群益的交易日)。Daily bars, by Capital's trading day.</summary>
    Day = 1,

    /// <summary>週 K。Weekly bars.</summary>
    Week = 2,

    /// <summary>月 K。Monthly bars.</summary>
    Month = 3,
}
