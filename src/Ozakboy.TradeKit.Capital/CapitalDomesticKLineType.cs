namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 國內歷史 K 線類型(<c>SKQuoteLib_RequestKLineAMByDate</c> 的 sKLineType;只有這兩種實測過)。
/// The domestic historical K-line type (sKLineType of SKQuoteLib_RequestKLineAMByDate); only these two were measured.
/// </summary>
/// <remarks>
/// 值就是群益的 sKLineType 原始代碼(0、4),不是旗標。The values are Capital's raw sKLineType codes, not flags.
/// </remarks>
#pragma warning disable CA1027 // 值照群益代碼(0 分、4 日),剛好像 2 的次方,不是旗標。The values are Capital's codes and only look like flags.
public enum CapitalDomesticKLineType
#pragma warning restore CA1027
{
    /// <summary>
    /// 1 分 K。群益給的時間是那一分鐘的<b>結束</b>(台北時間),<see cref="CapitalDomesticBar.OpenTimeUtc"/> 已減 1 分鐘並轉 UTC。
    /// One-minute bars. Capital gives the <b>end</b> of the minute in Taipei time; OpenTimeUtc is already one minute earlier
    /// and in UTC.
    /// </summary>
    Minute = 0,

    /// <summary>
    /// 完整日線(群益 sKLineType = 4),日期是交易日。
    /// Full daily bars (Capital's sKLineType 4), dated by trading day.
    /// </summary>
    Day = 4,
}
