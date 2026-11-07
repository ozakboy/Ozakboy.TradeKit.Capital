namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 國內期貨歷史 K 線的盤別(<c>SKQuoteLib_RequestKLineAMByDate</c> 的 sTradeSession)。
/// The session of domestic futures historical K-lines (sTradeSession of SKQuoteLib_RequestKLineAMByDate).
/// </summary>
/// <remarks>
/// <para>
/// 起訖日期是<b>交易日</b>:全盤查 2026-11-02(週一)會從 2026/10/30(週五)15:01 開始,因為週五夜盤歸下一個交易日(2026-11-07 實測)。
/// The dates are <b>trading days</b>: a full-session query for Monday 2026-11-02 starts at Friday 2026/10/30 15:01,
/// because Friday's night session belongs to the next trading day (measured 2026-11-07).
/// </para>
/// <para>
/// <b>收盤價的意義依盤別而不同:</b>全盤分 K 的日盤最後一根(13:45;結算日 13:30)的收盤,以及全盤日 K 的收盤,實測是期交所的
/// <b>結算價</b>;AM 盤日 K 的收盤才是最後成交價(2026-11-06 台指:最後成交 39786、結算 39652)。本套件回原始資料、不修正,由呼叫端決定。
/// <b>The meaning of the close depends on the session:</b> the close of the last day-session minute bar (13:45, or
/// 13:30 on settlement days) of a full-session query, and the close of a full-session daily bar, were measured to be
/// TAIFEX's <b>settlement price</b>; only the AM-session daily close is the last traded price (TAIEX futures on
/// 2026-11-06: last 39786, settlement 39652). This package returns the raw data unchanged; the caller decides.
/// </para>
/// </remarks>
public enum CapitalTradeSession
{
    /// <summary>
    /// 全盤:含前一個交易日 15:00 起的夜盤(15:01–05:00)與當日日盤(08:46–13:45)。
    /// Full session: the night session from 15:00 of the previous trading day (15:01–05:00) plus the day session
    /// (08:46–13:45).
    /// </summary>
    Full = 0,

    /// <summary>
    /// AM 盤:只有日盤(08:46–13:45)。
    /// AM session: the day session only (08:46–13:45).
    /// </summary>
    AmOnly = 1,
}
