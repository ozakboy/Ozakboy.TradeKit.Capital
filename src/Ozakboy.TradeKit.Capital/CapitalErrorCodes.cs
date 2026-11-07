namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 本套件回傳的所有錯誤碼。字串是公開契約,發佈後不再變更 —— 上層的重試、告警與畫面判斷都依它分支。
/// Every error code this package returns. The strings are part of the public contract and do not change once
/// published, because callers branch retry, alerting and display on them.
/// </summary>
public static class CapitalErrorCodes
{
    /// <summary>
    /// 設定不合法(宿主路徑空白、逾時非正數等)。
    /// A setting is invalid (blank host path, non-positive timeout and so on).
    /// </summary>
    public const string InvalidOptions = "capital.options.invalid";

    /// <summary>
    /// 帳密格式不合法(身分證字號須為 1 個英文字母 + 9 個數字、密碼不可空白)。訊息不回顯任何字元。
    /// The credentials are malformed (the ID must be one letter and nine digits, the password must not be blank). The
    /// message echoes no character.
    /// </summary>
    public const string InvalidCredentials = "capital.credentials.invalid";

    /// <summary>
    /// 呼叫參數不合法,請求沒有送出。
    /// An argument is invalid; nothing was sent.
    /// </summary>
    public const string InvalidArgument = "capital.argument.invalid";

    /// <summary>
    /// 宿主執行檔不存在。
    /// The host executable does not exist.
    /// </summary>
    public const string HostNotFound = "capital.host.not_found";

    /// <summary>
    /// 宿主行程啟動失敗。
    /// The host process could not be started.
    /// </summary>
    public const string HostStartFailed = "capital.host.start_failed";

    /// <summary>
    /// 宿主在 <see cref="CapitalHostOptions.StartTimeout"/> 內沒有就緒,已終止。
    /// The host was not ready within <see cref="CapitalHostOptions.StartTimeout"/> and was killed.
    /// </summary>
    public const string HostStartTimeout = "capital.host.start_timeout";

    /// <summary>
    /// 宿主行程非預期結束(結束碼在 <see cref="CapitalErrorDataKeys.ExitCode"/>);進行中的請求一併以此失敗。
    /// The host process ended unexpectedly (exit code in <see cref="CapitalErrorDataKeys.ExitCode"/>); requests in flight
    /// fail with this too.
    /// </summary>
    public const string HostExited = "capital.host.exited";

    /// <summary>
    /// 工作階段不在可以送請求的狀態(只有 Ready 與 Disconnected 可以)。
    /// The session is not in a state that accepts requests (only Ready and Disconnected do).
    /// </summary>
    public const string HostNotReady = "capital.host.not_ready";

    /// <summary>
    /// 正在啟動或停止中,這個操作不能同時進行。
    /// A start or stop is in progress, so this operation cannot run now.
    /// </summary>
    public const string InvalidState = "capital.host.invalid_state";

    /// <summary>
    /// 宿主送來讀不懂的回應(協定不符)。
    /// The host sent a response that could not be read (protocol mismatch).
    /// </summary>
    public const string ProtocolError = "capital.host.protocol";

    /// <summary>
    /// 群益登入失敗,<b>不會重試</b>。群益代碼在 <see cref="CapitalErrorDataKeys.Code"/>(300 密碼錯、306 身分證錯、
    /// 307 鎖定、600 憑證錯、604 憑證過期),鎖定時 <see cref="CapitalErrorDataKeys.Locked"/> 為 <see langword="true"/>。
    /// Capital's login failed and is <b>never retried</b>. Capital's code is in <see cref="CapitalErrorDataKeys.Code"/>
    /// (300 wrong password, 306 wrong ID, 307 locked, 600 certificate error, 604 certificate expired);
    /// <see cref="CapitalErrorDataKeys.Locked"/> is true when locked.
    /// </summary>
    public const string LoginFailed = "capital.login.failed";

    /// <summary>
    /// 宿主沒收到帳密(不應發生:用戶端一律以環境變數傳入)。宿主沒有呼叫 Login。
    /// The host received no credentials (should not happen; the client always passes them). Login was not called.
    /// </summary>
    public const string CredentialsMissing = "capital.credentials.missing";

    /// <summary>
    /// SKCOM 沒有註冊(HRESULT 0x80040154):請以系統管理員身分執行 SKCOM x64 資料夾的 install.bat。
    /// SKCOM is not registered (0x80040154): run install.bat of the SKCOM x64 folder as an administrator.
    /// </summary>
    public const string SkComNotRegistered = "capital.skcom.not_registered";

    /// <summary>
    /// SKCOM 已註冊,但登錄檔指向的 DLL 不存在(HRESULT 0x8007007E):資料夾被搬走或刪掉,重新註冊。
    /// SKCOM is registered but the DLL it points to is missing (0x8007007E): the folder moved; register it again.
    /// </summary>
    public const string SkComFileMissing = "capital.skcom.file_missing";

    /// <summary>
    /// 建立 SKCOM 物件失敗(其他 HRESULT,在 <see cref="CapitalErrorDataKeys.HResult"/>)。
    /// Creating the SKCOM objects failed with another HRESULT, in <see cref="CapitalErrorDataKeys.HResult"/>.
    /// </summary>
    public const string SkComCreateFailed = "capital.skcom.create_failed";

    /// <summary>
    /// 連不上海外報價主機(60 秒內沒有 OnConnect 3001)。連線類錯誤,可以重試。
    /// The overseas quote server could not be reached (no OnConnect 3001 within 60 seconds). A connection error; retryable.
    /// </summary>
    public const string ConnectTimeout = "capital.connect.timeout";

    /// <summary>
    /// EnterMonitorLONG 直接回了錯誤代碼(在 <see cref="CapitalErrorDataKeys.Code"/>)。
    /// EnterMonitorLONG returned an error code, in <see cref="CapitalErrorDataKeys.Code"/>.
    /// </summary>
    public const string ConnectRejected = "capital.connect.rejected";

    /// <summary>
    /// 宿主內部缺陷(結束碼 1)。
    /// An internal host defect (exit code 1).
    /// </summary>
    public const string HostInternalError = "capital.host.internal_error";

    /// <summary>
    /// 請求在時限內沒有回應。
    /// The request was not answered in time.
    /// </summary>
    public const string RequestTimeout = "capital.request.timeout";

    /// <summary>
    /// 呼叫端取消了請求。
    /// The caller cancelled the request.
    /// </summary>
    public const string RequestCancelled = "capital.request.cancelled";

    /// <summary>
    /// 宿主認為請求格式不合法(協定層級的缺陷)。
    /// The host found the request malformed (a protocol-level defect).
    /// </summary>
    public const string RequestInvalid = "capital.request.invalid";

    /// <summary>
    /// 宿主不認得這個 method(宿主版本與用戶端不符)。
    /// The host does not know the method (host and client versions differ).
    /// </summary>
    public const string UnknownMethod = "capital.request.unknown_method";

    /// <summary>
    /// 海外報價主機目前斷線(宿主正在重連),商品與 K 線請求不能送。
    /// The overseas quote server is disconnected right now (the host is reconnecting); products and K-lines cannot be asked.
    /// </summary>
    public const string NotConnected = "capital.not_connected";

    /// <summary>
    /// 商品清單或明細在 60 秒內沒有收到 <c>##</c> 結尾。
    /// The product list or details did not reach the "##" end within 60 seconds.
    /// </summary>
    public const string ProductsTimeout = "capital.products.timeout";

    /// <summary>
    /// 商品清單請求被群益拒絕(代碼在 <see cref="CapitalErrorDataKeys.Code"/>)。
    /// The product request was rejected by Capital (code in <see cref="CapitalErrorDataKeys.Code"/>).
    /// </summary>
    public const string ProductsRejected = "capital.products.rejected";

    /// <summary>
    /// 訂閱被群益拒絕(代碼在 <see cref="CapitalErrorDataKeys.Code"/>、商品在 <see cref="CapitalErrorDataKeys.Symbol"/>)。
    /// The subscription was rejected by Capital (code in <see cref="CapitalErrorDataKeys.Code"/>, symbols in
    /// <see cref="CapitalErrorDataKeys.Symbol"/>).
    /// </summary>
    public const string SubscribeRejected = "capital.subscribe.rejected";

    /// <summary>
    /// K 線請求被群益拒絕(例如 1042 = 不支援的 K 線類型;國內代碼不存在或沒有權限時實測 9999、3023)。
    /// The K-line request was rejected by Capital (for example 1042, an unsupported K-line type; an unknown domestic
    /// code or a missing permission was measured as 9999 or 3023).
    /// </summary>
    public const string KLineRejected = "capital.kline.rejected";

    /// <summary>
    /// 國內 K 線請求在 60 秒內沒有收到結束事件(<c>OnKLineComplete</c>)。可以稍後再試;已收到的筆數在 <c>Data["received"]</c>。
    /// The domestic K-line request saw no end event (OnKLineComplete) within 60 seconds. Retryable later; the rows
    /// received so far are counted in Data["received"].
    /// </summary>
    public const string KLineTimeout = "capital.kline.timeout";

    /// <summary>
    /// 國內報價目前不可用:<see cref="CapitalErrorDataKeys.DomesticState"/> 是當時的國內狀態(<c>unavailable</c> /
    /// <c>disconnected</c>),<see cref="CapitalErrorDataKeys.Reason"/> 是原因(<see cref="CapitalDomesticReasons"/>),
    /// EnterMonitorLONG 被拒時群益代碼在 <see cref="CapitalErrorDataKeys.Code"/>。海外報價不受影響。
    /// The domestic quote service is not available: DomesticState holds the domestic state at the time, Reason the
    /// reason, and Code Capital's code when EnterMonitorLONG was rejected. Overseas quotes are unaffected.
    /// </summary>
    public const string DomesticUnavailable = "capital.domestic.unavailable";

    /// <summary>
    /// K 線文字讀不懂(格式不是 <c>yyyy/MM/dd[ HH:mm], O, H, L, C, V</c>)。
    /// A K-line row could not be read (not <c>yyyy/MM/dd[ HH:mm], O, H, L, C, V</c>).
    /// </summary>
    public const string KLineMalformed = "capital.kline.malformed";

    /// <summary>
    /// 交易所沒有已知時區,時間無法轉 UTC。
    /// The exchange has no known time zone, so its times cannot be converted to UTC.
    /// </summary>
    public const string UnknownExchange = "capital.clock.unknown_exchange";

    /// <summary>
    /// 警告(只記日誌,不是回傳值):報價的分母(nDenominator)不是 1,以小數位數照常換算。
    /// A warning, logged only: a quote's denominator (nDenominator) is not 1; prices are still scaled by decimals.
    /// </summary>
    public const string PriceFractionUnsupported = "capital.price.fraction_unsupported";
}
