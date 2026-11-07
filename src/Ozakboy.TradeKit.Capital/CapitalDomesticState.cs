namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 國內報價(SKQuoteLib,目前只用來查國內期貨歷史 K 線)的狀態。與海外的 <see cref="CapitalHostState"/> 分開:
/// 國內不可用不影響海外報價、不讓 <see cref="CapitalHostSession.StartAsync"/> 失敗。
/// The state of the domestic quote side (SKQuoteLib, used only for domestic futures K-lines for now). Separate from the
/// overseas <see cref="CapitalHostState"/>: the domestic side being unavailable never affects overseas quotes and never
/// fails StartAsync.
/// </summary>
public enum CapitalDomesticState
{
    /// <summary>宿主沒在跑,或海外還沒就緒(國內在海外就緒後才連)。No host, or the overseas side is not ready yet.</summary>
    NotStarted = 0,

    /// <summary>已送 EnterMonitorLONG,等商品基本資料完成(3003,實測約 0.5 秒);這段期間的 K 線請求會等它。Waiting for 3003.</summary>
    Connecting = 1,

    /// <summary>就緒,可以查國內 K 線。Ready for domestic K-line requests.</summary>
    Ready = 2,

    /// <summary>國內報價主機斷線(3002),宿主正以 5、10、30、60 秒退避重連。Disconnected; the host reconnects with backoff.</summary>
    Disconnected = 3,

    /// <summary>
    /// 不可用,不會自動重試(原因見 <see cref="CapitalDomesticReasons"/>;例如帳號沒有國內報價權限)。
    /// Unavailable and never retried automatically (see <see cref="CapitalDomesticReasons"/>).
    /// </summary>
    Unavailable = 4,
}
