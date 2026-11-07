namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// <c>serve</c> 模式的結束碼(與用戶端的監督邏輯是同一份契約)。
/// Exit codes of the serve mode; the client's supervision depends on them.
/// </summary>
internal static class ServeExitCodes
{
    /// <summary>收到 shutdown 或 stdin 關閉後正常結束。Clean exit after shutdown or end of stdin.</summary>
    public const int Success = 0;

    /// <summary>宿主內部缺陷(未預期的例外)。An internal defect (an unexpected exception).</summary>
    public const int InternalError = 1;

    /// <summary>缺少帳號或密碼環境變數;沒有呼叫 Login。Missing credentials; Login was never called.</summary>
    public const int MissingCredentials = 2;

    /// <summary>SKCOM 物件建不起來(沒註冊、DLL 不在)。The SKCOM objects could not be created.</summary>
    public const int SkComUnavailable = 3;

    /// <summary>登入失敗(不重試)。Login failed (never retried).</summary>
    public const int LoginFailed = 4;

    /// <summary>連不上海外報價主機。The overseas quote server could not be reached.</summary>
    public const int ConnectFailed = 5;
}

/// <summary>
/// 協定上 <c>status</c> 事件的 <c>state</c> 與 <c>reason</c> 字串。
/// The state and reason strings of the protocol's status event.
/// </summary>
internal static class ServeStates
{
    public const string LoggingIn = "logging_in";
    public const string LoginFailed = "login_failed";
    public const string Connecting = "connecting";
    public const string Ready = "ready";
    public const string Disconnected = "disconnected";
    public const string Faulted = "faulted";

    public const string ReasonMissingCredentials = "missing_credentials";
    public const string ReasonNotRegistered = "skcom_not_registered";
    public const string ReasonFileMissing = "skcom_file_missing";
    public const string ReasonCreateFailed = "skcom_create_failed";
    public const string ReasonConnectTimeout = "connect_timeout";
    public const string ReasonConnectRejected = "connect_rejected";
    public const string ReasonInternalError = "internal_error";
}

/// <summary>
/// 協定上國內報價的狀態(<c>status</c> 事件與 <c>status</c> 回應的 <c>domestic</c> / <c>domesticReason</c>;與用戶端
/// <c>CapitalDomesticReasons</c> 一致)。國內不可用不影響海外的 ready 與結束碼。
/// The domestic quote states on the wire (domestic and domesticReason of the status event and the status response;
/// they match the client's CapitalDomesticReasons). The domestic side being unavailable never affects the overseas
/// ready or the exit code.
/// </summary>
internal static class DomesticStates
{
    public const string Connecting = "connecting";
    public const string Ready = "ready";
    public const string Disconnected = "disconnected";
    public const string Unavailable = "unavailable";

    /// <summary>EnterMonitorLONG 回非 0(代碼在 domesticCode)。EnterMonitorLONG answered non-zero.</summary>
    public const string ReasonEnterRejected = "enter_rejected";

    /// <summary>時限內沒有收到 3003(商品基本資料完成)。No 3003 within the limit.</summary>
    public const string ReasonReadyTimeout = "ready_timeout";

    /// <summary>收到 3002,正在退避重連。A 3002 arrived; reconnecting with backoff.</summary>
    public const string ReasonConnectionLost = "connection_lost";
}

/// <summary>
/// 協定上回應錯誤的 <c>code</c>(與用戶端 <c>CapitalErrorCodes</c> 的同名常數一致)。
/// Error codes on the wire; they match the client's CapitalErrorCodes.
/// </summary>
internal static class WireErrorCodes
{
    public const string RequestInvalid = "capital.request.invalid";
    public const string UnknownMethod = "capital.request.unknown_method";
    public const string NotConnected = "capital.not_connected";
    public const string ProductsTimeout = "capital.products.timeout";
    public const string ProductsRejected = "capital.products.rejected";
    public const string SubscribeRejected = "capital.subscribe.rejected";
    public const string KLineRejected = "capital.kline.rejected";
    public const string KLineTimeout = "capital.kline.timeout";
    public const string DomesticUnavailable = "capital.domestic.unavailable";
}

/// <summary>
/// <c>serve</c> 模式的環境變數名稱。
/// Environment variable names of the serve mode.
/// </summary>
internal static class ServeEnvironmentNames
{
    public const string LoginId = "CAPITAL_LOGIN_ID";
    public const string Password = "CAPITAL_PASSWORD";
    public const string LogDirectory = "CAPITAL_LOG_DIR";

    /// <summary>父行程 PID(0.2.0 起用戶端會帶;不是機密)。The parent PID, passed by the client since 0.2.0; not a secret.</summary>
    public const string ParentPid = "CAPITAL_PARENT_PID";
}
