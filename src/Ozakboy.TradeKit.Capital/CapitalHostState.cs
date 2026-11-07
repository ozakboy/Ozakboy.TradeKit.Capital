namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// <see cref="CapitalHostSession"/> 的狀態。
/// The state of <see cref="CapitalHostSession"/>.
/// </summary>
public enum CapitalHostState
{
    /// <summary>沒有宿主在跑(初始狀態,或 <see cref="CapitalHostSession.StopAsync"/> 之後)。No host running.</summary>
    Stopped = 0,

    /// <summary>宿主行程啟動中(含意外結束後的重啟)。The host process is starting, including a restart.</summary>
    Starting = 1,

    /// <summary>登入群益中。Logging in to Capital.</summary>
    LoggingIn = 2,

    /// <summary>連海外報價主機中。Connecting to the overseas quote server.</summary>
    Connecting = 3,

    /// <summary>就緒,可以送請求、會收到事件。Ready: requests are accepted and events flow.</summary>
    Ready = 4,

    /// <summary>報價主機斷線(3002),宿主正在退避重連;訂閱照收,商品與 K 線請求會失敗。Disconnected; the host is reconnecting.</summary>
    Disconnected = 5,

    /// <summary>登入失敗,不會自動重試。Login failed; never retried automatically.</summary>
    LoginFailed = 6,

    /// <summary>無法繼續(SKCOM 不可用、重啟次數用完等),不會再自動重啟。Cannot go on; no further automatic restarts.</summary>
    Faulted = 7,
}
