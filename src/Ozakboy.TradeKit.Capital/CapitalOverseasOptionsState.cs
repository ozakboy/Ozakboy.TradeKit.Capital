namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 海外選擇權報價(SKOOQuoteLib)的狀態,與海外的 <see cref="CapitalHostState"/>、國內的 <see cref="CapitalDomesticState"/> 都分開:
/// 不可用時不影響其他報價,也不讓 <see cref="CapitalHostSession.StartAsync"/> 失敗。原因字串與 <see cref="CapitalDomesticReasons"/> 相同。
/// The state of the overseas option quote side, separate from the overseas and the domestic states; being unavailable
/// never affects the other feeds or StartAsync. The reason strings are the ones in <see cref="CapitalDomesticReasons"/>.
/// </summary>
public enum CapitalOverseasOptionsState
{
    /// <summary>宿主沒在跑,或海外還沒就緒(海外就緒後才連)。No host, or the overseas side is not ready yet.</summary>
    NotStarted = 0,

    /// <summary>已送 EnterMonitorLONG,等連線事件;這段期間的請求由宿主等著。Waiting for the connect event.</summary>
    Connecting = 1,

    /// <summary>就緒。Ready.</summary>
    Ready = 2,

    /// <summary>斷線,宿主以 5、10、30、60 秒退避重連。Disconnected; the host reconnects with backoff.</summary>
    Disconnected = 3,

    /// <summary>不可用,不會自動重試。Unavailable and never retried automatically.</summary>
    Unavailable = 4,
}
