namespace Ozakboy.TradeKit.Capital.Host.Serve;

// 0.3.0:期貨帳務查詢(SKOrderLib,唯讀)。只有這裡列的查詢;SKOrderLib 上會改變委託、部位或資金的方法在 COM 定義裡都是 Unused 佔位,
// 根本呼叫不到(SkComOrderInteropSafetyTests 以反射核對)。回傳的原文含帳號、身分證字號與戶名:協定層絕不把它寫進 stderr。
// 0.3.0: read-only futures account queries. Only these queries exist; every order-changing method of SKOrderLib is an
// uncallable placeholder in the COM definitions. The raw replies carry account numbers, the holder's ID and name, and
// the protocol layer never writes them to stderr.
internal partial interface ISkQuoteSource
{
    /// <summary>帳號一筆(<c>OnAccount</c>,手冊 4-2-a:登入帳號, 帳號資訊原文)。One account row (login ID, raw data).</summary>
    event Action<string, string>? AccountRow;

    /// <summary>
    /// 查詢結果一則(事件名稱, 原文):<c>OnFutureRights</c>、<c>OnOpenInterest</c>、<c>OnOverSeaFutureRight</c>、<c>OnOFOpenInterestGWReport</c>…
    /// One query reply (event name, raw text).
    /// </summary>
    event Action<string, string>? AccountReply;

    /// <summary>查詢狀態(事件名稱, 0 成功 / 1 失敗, 訊息;手冊 4-2-t–w)。A query status (event name, 0 or 1, message).</summary>
    event Action<string, int, string>? AccountStatus;

    /// <summary>
    /// 建立 SKOrderLib 並掛事件(在登入前,probe-full 的順序);<c>SKOrderLib_Initialize</c> 延到第一次帳務請求才呼叫。
    /// Creates SKOrderLib and hooks its events before login, as probe-full does; Initialize waits for the first account request.
    /// </summary>
    /// <returns>成功時為 <see langword="null"/>。Null on success.</returns>
    SkOpenFailure? OpenAccounts();

    /// <summary>下單物件初始化(<c>SKOrderLib_Initialize</c>,手冊 4-2-1;只用來查帳務)。Initialises SKOrderLib, for queries only.</summary>
    /// <returns>群益代碼。Capital's code.</returns>
    int InitializeAccounts();

    /// <summary>取帳號清單(<c>GetUserAccount</c>,手冊 4-2-2;由 <see cref="AccountRow"/> 回傳)。Requests the account list.</summary>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestAccounts();

    /// <summary>讀憑證(<c>ReadCertByID</c>,手冊 4-2-3)。Reads the certificate.</summary>
    /// <param name="loginId">登入帳號。The login ID.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int ReadCertificate(string loginId);

    /// <summary>國內權益數(<c>GetFutureRights</c>,手冊 4-2-41)。Domestic futures rights.</summary>
    /// <param name="loginId">登入帳號。The login ID.</param>
    /// <param name="account">完整帳號(IB + 帳號)。The full account.</param>
    /// <param name="coinType">0 全幣別、1 基幣、2 人民幣。0 all, 1 base currency, 2 RMB.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestFutureRights(string loginId, string account, short coinType);

    /// <summary>國內未平倉(<c>GetOpenInterestGW</c>,手冊 4-2-89)。Domestic open interest.</summary>
    /// <param name="loginId">登入帳號。The login ID.</param>
    /// <param name="account">完整帳號。The full account.</param>
    /// <param name="format">回傳格式(手冊只列 1)。The format; the manual lists only 1.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestOpenInterest(string loginId, string account, int format);

    /// <summary>海外權益數(<c>GetRequestOverSeaFutureRight</c>,手冊 4-2-45)。Overseas futures rights.</summary>
    /// <param name="loginId">登入帳號。The login ID.</param>
    /// <param name="account">完整帳號。The full account.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestOverseaFutureRights(string loginId, string account);

    /// <summary>海外未平倉(<c>GetOverseaFutureOpenInterestGW</c>,手冊 4-2-76;1 彙總、2 明細)。Overseas open interest.</summary>
    /// <param name="loginId">登入帳號。The login ID.</param>
    /// <param name="account">完整帳號。The full account.</param>
    /// <param name="format">1 彙總、2 明細。1 summary, 2 detail.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestOverseaOpenInterest(string loginId, string account, int format);

    /// <summary>委託回報查詢(<c>GetOrderReport</c>,手冊 4-2-97;阻塞式、直接回字串、每次間隔 5 秒)。The blocking order report query.</summary>
    /// <param name="loginId">登入帳號。The login ID.</param>
    /// <param name="account">完整帳號。The full account.</param>
    /// <param name="format">1–9。</param>
    /// <returns>回報原文。The raw report.</returns>
    string GetOrderReport(string loginId, string account, int format);

    /// <summary>成交回報查詢(<c>GetFulfillReport</c>,手冊 4-2-98;阻塞式、每次間隔 5 秒)。The blocking fill report query.</summary>
    /// <param name="loginId">登入帳號。The login ID.</param>
    /// <param name="account">完整帳號。The full account.</param>
    /// <param name="format">1–5。</param>
    /// <returns>回報原文。The raw report.</returns>
    string GetFulfillReport(string loginId, string account, int format);
}
