namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// 把 SKCOM(登入、海外報價連線、商品、訂閱、K 線與事件,以及國內報價的連線與歷史 K 線)隔在介面後面:協定層
/// <see cref="ServeHost"/> 只認這個介面,真實作是 <c>SkComQuoteSource</c>,測試與假宿主以假實作驅動。回傳值一律是群益的原始代碼(0 = 成功)。
/// Keeps SKCOM (login, the overseas quote connection, products, subscriptions, K-lines and events, plus the domestic
/// quote connection and its historical K-lines) behind an interface.
/// The protocol layer only knows this interface; the real implementation is <c>SkComQuoteSource</c> and tests and the
/// fake host drive a fake one. Return values are Capital's raw codes (0 = success).
/// </summary>
/// <remarks>
/// 事件可能從任何執行緒觸發(真 SKCOM 是 STA 訊息迴圈,假實作可能是計時器執行緒),協定層自己負責同步。
/// Events may be raised on any thread (the real SKCOM uses the STA message loop, a fake may use a timer thread); the
/// protocol layer does its own synchronisation.
/// </remarks>
internal interface ISkQuoteSource : IDisposable
{
    /// <summary>公告(<c>OnReplyMessage</c> 的內容,不含登入帳號參數)。An announcement, without the login ID argument.</summary>
    event Action<string>? Announcement;

    /// <summary>連線狀態(<c>OnConnect</c> 的 nCode:3001 連線、3002 斷線)。Connection state (3001 connected, 3002 disconnected).</summary>
    event Action<int>? Connection;

    /// <summary>商品清單一筆(<c>OnOverseaProducts</c>),以 <c>##</c> 開頭那筆結尾。One product row, ending with the "##" row.</summary>
    event Action<string>? Product;

    /// <summary>商品明細一筆(<c>OnOverseaProductsDetail</c>),以 <c>##</c> 開頭那筆結尾。One product detail row.</summary>
    event Action<string>? ProductDetail;

    /// <summary>歷史 K 一筆(商品代碼〔不含交易所〕, 資料)。One K-line row (code without exchange, data).</summary>
    event Action<string, string>? KLine;

    /// <summary>報價異動(商品索引)。A quote changed (stock index).</summary>
    event Action<int>? Quote;

    /// <summary>成交(即時或訂閱當下的回補)。A tick, live or replayed at subscription.</summary>
    event Action<SkTickData>? Tick;

    /// <summary>
    /// 建立 SKCOM 物件並掛上事件。
    /// Creates the SKCOM objects and attaches the events.
    /// </summary>
    /// <returns>成功時為 <see langword="null"/>,否則為失敗原因。Null on success, otherwise the failure.</returns>
    SkOpenFailure? Open();

    /// <summary>SKCOM 自己的紀錄目錄(<c>SKCenterLib_SetLogPath</c>)。SKCOM's own log directory.</summary>
    /// <param name="path">目錄。The directory.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int SetLogPath(string path);

    /// <summary>登入(<c>SKCenterLib_Login</c>)。絕不重試。Logs in; never retried.</summary>
    /// <param name="loginId">身分證字號(大寫)。The login ID, upper case.</param>
    /// <param name="password">密碼。The password.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int Login(string loginId, string password);

    /// <summary>代碼的說明文字(<c>SKCenterLib_GetReturnCodeMessage</c>)。The text for a code.</summary>
    /// <param name="code">群益代碼。Capital's code.</param>
    /// <returns>說明文字。The text.</returns>
    string GetReturnCodeMessage(int code);

    /// <summary>最後一筆紀錄(<c>SKCenterLib_GetLastLogInfo</c>;可能含登入帳號,輸出前要遮)。The last log line; may hold the login ID.</summary>
    /// <returns>紀錄文字。The text.</returns>
    string GetLastLogInfo();

    /// <summary>連海外報價主機(<c>SKOSQuoteLib_EnterMonitorLONG</c>)。Connects to the overseas quote server.</summary>
    /// <returns>群益代碼。Capital's code.</returns>
    int EnterMonitor();

    /// <summary>離開海外報價主機(<c>SKOSQuoteLib_LeaveMonitor</c>)。Leaves the overseas quote server.</summary>
    /// <returns>群益代碼。Capital's code.</returns>
    int LeaveMonitor();

    /// <summary>是否連線(<c>SKOSQuoteLib_IsConnected</c>,1 = 連線)。Whether connected (1 = connected).</summary>
    /// <returns>群益回傳值。Capital's value.</returns>
    int IsConnected();

    /// <summary>報價連線狀態(<c>SKOSQuoteLib_GetQuoteStatus</c>)。The quote connection status.</summary>
    /// <param name="connections">連線數。Connection count.</param>
    /// <param name="outLimit">是否超限。Whether over the limit.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int GetQuoteStatus(out int connections, out bool outLimit);

    /// <summary>要商品清單(<c>SKOSQuoteLib_RequestOverseaProducts</c>)。Requests the product list.</summary>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestOverseaProducts();

    /// <summary>要商品明細(<c>SKOSQuoteLib_GetOverseaProductDetail</c>)。Requests product details.</summary>
    /// <param name="type">明細種類(實測用 1)。Detail type (1 as measured).</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int GetOverseaProductDetail(short type);

    /// <summary>訂報價(<c>SKOSQuoteLib_RequestStocks</c>,page = -1 自動配)。Subscribes quotes with an automatic page.</summary>
    /// <param name="stockNos">以 <c>#</c> 串接的「交易所,代碼」。"Exchange,code" joined with '#'.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestStocks(string stockNos);

    /// <summary>訂成交(<c>SKOSQuoteLib_RequestTicks</c>,page = -1 自動配)。Subscribes ticks with an automatic page.</summary>
    /// <param name="stockNo">「交易所,代碼」。"Exchange,code".</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestTicks(string stockNo);

    /// <summary>要歷史 K(<c>SKOSQuoteLib_RequestKLineByDate</c>)。Requests historical K-lines.</summary>
    /// <param name="stockNo">「交易所,代碼」。"Exchange,code".</param>
    /// <param name="type">0 分、1 日、2 週、3 月。0 minute, 1 day, 2 week, 3 month.</param>
    /// <param name="startDate">yyyyMMdd。Start date.</param>
    /// <param name="endDate">yyyyMMdd。End date.</param>
    /// <param name="minuteNumber">幾分 K(實測用 1)。Minutes per bar (1 as measured).</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestKLineByDate(string stockNo, short type, string startDate, string endDate, short minuteNumber);

    /// <summary>以索引取報價(<c>SKOSQuoteLib_GetStockByIndexNineDigitLONG</c>)。Reads a quote by index.</summary>
    /// <param name="index">商品索引。Stock index.</param>
    /// <param name="quote">報價。The quote.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int GetStockByIndex(int index, out SkQuoteData quote);

    // ── 國內報價(SKQuoteLib):目前只用歷史 K 線。Domestic quotes (SKQuoteLib); only historical K-lines for now. ──

    /// <summary>
    /// 國內報價連線狀態(<c>OnConnection</c> 的 nKind:3001 已連線、3002 斷線、3003 商品基本資料完成〔之後才能查〕)。
    /// The domestic connection state (nKind of OnConnection: 3001 connected, 3002 disconnected, 3003 product data
    /// loaded, after which queries work).
    /// </summary>
    event Action<int>? DomesticConnection;

    /// <summary>國內歷史 K 一筆(<c>OnNotifyKLineData</c>:商品代碼, 資料)。One domestic K-line row (code, data).</summary>
    event Action<string, string>? DomesticKLine;

    /// <summary>
    /// 國內歷史 K 一個請求送完(<c>OnKLineComplete</c>,實測內容 <c>##</c>;每個請求一則,0 筆時也有)。
    /// One domestic K-line request finished (OnKLineComplete, measured "##"; one per request, even with no rows).
    /// </summary>
    event Action<string>? DomesticKLineComplete;

    /// <summary>
    /// 建立國內報價物件(SKQuoteLib)並掛上事件。在 <see cref="Open"/> 成功之後、登入之前呼叫(探針實測可行的順序)。
    /// Creates the domestic quote object and attaches its events; called after <see cref="Open"/> succeeded and before
    /// login, the order the probe measured to work.
    /// </summary>
    /// <returns>成功時為 <see langword="null"/>,否則為失敗原因。Null on success, otherwise the failure.</returns>
    SkOpenFailure? OpenDomestic();

    /// <summary>連國內報價主機(<c>SKQuoteLib_EnterMonitorLONG</c>)。Connects to the domestic quote server.</summary>
    /// <returns>群益代碼。Capital's code.</returns>
    int EnterDomesticMonitor();

    /// <summary>離開國內報價主機(<c>SKQuoteLib_LeaveMonitor</c>)。Leaves the domestic quote server.</summary>
    /// <returns>群益代碼。Capital's code.</returns>
    int LeaveDomesticMonitor();

    /// <summary>
    /// 要國內歷史 K(<c>SKQuoteLib_RequestKLineAMByDate</c>)。回 0 表示送出;代碼不存在或沒權限立即回非 0(實測 9999、3023)。
    /// Requests domestic historical K-lines; 0 means sent, and an unknown code or a missing permission answers non-zero
    /// at once (measured 9999 and 3023).
    /// </summary>
    /// <param name="stockNo">商品代碼(例如 <c>TX00</c>)。The code, for example TX00.</param>
    /// <param name="kLineType">0 分線、4 完整日線。0 minute, 4 full daily.</param>
    /// <param name="outType">輸出格式(1 = 新版,實測用 1)。Output format; 1 = new format, as measured.</param>
    /// <param name="tradeSession">0 全盤(含前一晚夜盤)、1 AM 盤。0 full session, 1 AM session.</param>
    /// <param name="startDate">起始交易日 yyyyMMdd。First trading day.</param>
    /// <param name="endDate">結束交易日 yyyyMMdd。Last trading day.</param>
    /// <param name="minuteNumber">幾分 K(實測用 1)。Minutes per bar (1 as measured).</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestDomesticKLine(string stockNo, short kLineType, short outType, short tradeSession, string startDate, string endDate, short minuteNumber);
}

/// <summary>
/// <see cref="ISkQuoteSource.Open"/> 的失敗原因。
/// Why <see cref="ISkQuoteSource.Open"/> failed.
/// </summary>
/// <param name="Reason"><c>skcom_not_registered</c> / <c>skcom_file_missing</c> / <c>skcom_create_failed</c>。</param>
/// <param name="HResult">COM 的 HRESULT。The COM HRESULT.</param>
internal sealed record SkOpenFailure(string Reason, int HResult);

/// <summary>
/// 一筆報價(<c>SKFOREIGN_9LONG</c> 用得到的欄位),價格是原始整數。
/// One quote (the fields of <c>SKFOREIGN_9LONG</c> in use); prices are raw integers.
/// </summary>
internal readonly record struct SkQuoteData
{
    public string ExchangeNo { get; init; }

    public string StockNo { get; init; }

    public string StockName { get; init; }

    public int Decimals { get; init; }

    public int Denominator { get; init; }

    public int TradingDay { get; init; }

    public long Open { get; init; }

    public long High { get; init; }

    public long Low { get; init; }

    public long Close { get; init; }

    public long Settle { get; init; }

    public long Reference { get; init; }

    public long Bid { get; init; }

    public int BidQuantity { get; init; }

    public long Ask { get; init; }

    public int AskQuantity { get; init; }

    public long TotalQuantity { get; init; }

    public int TickQuantity { get; init; }
}

/// <summary>
/// 一筆成交(<c>OnNotifyTicksNineDigitLONG</c> / <c>OnNotifyHistoryTicksNineDigitLONG</c>)。
/// One tick.
/// </summary>
/// <param name="Index">商品索引。Stock index.</param>
/// <param name="Pointer">nPtr,每檔遞增。nPtr, increasing per stock.</param>
/// <param name="Date">yyyyMMdd(交易所當地)。Local date.</param>
/// <param name="Time">HHmmss(交易所當地)。Local time.</param>
/// <param name="Price">原始整數價格。Raw integer price.</param>
/// <param name="Quantity">量。Quantity.</param>
/// <param name="IsHistory">訂閱當下的回補。Replayed at subscription.</param>
internal readonly record struct SkTickData(int Index, int Pointer, int Date, int Time, long Price, int Quantity, bool IsHistory);
