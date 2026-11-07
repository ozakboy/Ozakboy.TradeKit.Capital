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
}
