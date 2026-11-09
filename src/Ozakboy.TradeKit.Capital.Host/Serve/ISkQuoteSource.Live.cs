namespace Ozakboy.TradeKit.Capital.Host.Serve;

// 0.3.0:SKCOM 版本、國內期貨 / 選擇權即時報價(SKQuoteLib)與海外五檔 / 十檔(SKOSQuoteLib)。手冊 V2.13.59 的章節號寫在各成員;
// 凡是手冊沒寫清楚的(欄位位置、時間語意、參數值)都標「待實測」,要對 probe-full 的輸出核對。
// 0.3.0: the SKCOM version, domestic futures and options live quotes, and overseas best 5 / best 10. Everything the
// manual leaves open is marked as still to be measured against the probe-full output.
internal partial interface ISkQuoteSource
{
    /// <summary>
    /// SKCOM 版本與位元(<c>SKCenterLib_GetSKAPIVersionAndBit</c>,手冊 4-1-10,例如 <c>2.13.59_x64</c>)。在 <see cref="Open"/> 之後、登入之前呼叫
    /// (probe-full 的順序)。
    /// The SKCOM version and bitness, read after Open and before login as probe-full does.
    /// </summary>
    /// <param name="loginId">登入帳號(手冊的參數;只留在宿主記憶體)。The login ID, as the manual asks; kept in host memory only.</param>
    /// <returns>版本文字。The version text.</returns>
    string GetApiVersion(string loginId);

    // ── 國內即時報價(SKQuoteLib)。Domestic live quotes. ──

    /// <summary>商品清單一則(<c>OnNotifyStockList</c>,手冊 4-4-h:市場別, 原文;<c>##</c> 開頭是結尾)。One stock-list chunk.</summary>
    event Action<short, string>? DomesticStockList;

    /// <summary>報價異動(<c>OnNotifyQuoteLONG</c>,手冊 4-4-k:市場別, 索引)。A domestic quote changed (market, index).</summary>
    event Action<short, int>? DomesticQuote;

    /// <summary>成交(即時或回補,手冊 4-4-l / 4-4-m)。A domestic tick, live or replayed.</summary>
    event Action<SkDomesticTick>? DomesticTick;

    /// <summary>五檔(<c>OnNotifyBest5LONG</c>,手冊 4-4-n,含衍生一檔與試算旗標)。The domestic best 5.</summary>
    event Action<SkBook>? DomesticBest5;

    /// <summary>即時分 K(<c>OnNotifyLiveKLineData</c>,手冊 4-4-u)。A domestic live K-line notification.</summary>
    event Action<SkLiveBar>? DomesticLiveBar;

    /// <summary>要商品清單(<c>SKQuoteLib_RequestStockList</c>,手冊 4-4-17:期貨 2、選擇權 3)。Requests a domestic stock list.</summary>
    /// <param name="market">市場別。The market number.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestDomesticStockList(short market);

    /// <summary>
    /// 訂國內報價(<c>SKQuoteLib_RequestStocks</c>,手冊 4-4-2:psPageNo 固定帶 1、代碼以逗號分隔、最多 100 檔;一個物件只有一組即時報價訂閱,
    /// 所以每次都送完整集合)。
    /// Subscribes domestic quotes with page 1 and comma-separated codes; one object holds one quote subscription, so the
    /// whole set is sent every time.
    /// </summary>
    /// <param name="codes">以逗號串接的代碼。Codes joined by commas.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestDomesticStocks(string codes);

    /// <summary>
    /// 訂國內成交、五檔與即時分 K(<c>SKQuoteLib_RequestTicks</c>,手冊 4-4-3:psPageNo 從 0 開始,一個 Page 一檔;手冊 4-4-1 一個物件 10 檔)。
    /// Subscribes domestic ticks, best 5 and live bars; pages start at 0 with one code per page.
    /// </summary>
    /// <param name="page">頁碼。The page.</param>
    /// <param name="code">代碼。The code.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestDomesticTicks(short page, string code);

    /// <summary>以市場別與索引取商品(<c>SKQuoteLib_GetStockByIndexLONG</c>,手冊 4-4-26)。Reads a domestic stock by market and index.</summary>
    /// <param name="market">市場別。The market number.</param>
    /// <param name="index">索引。The index.</param>
    /// <param name="stock">商品。The stock.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int GetDomesticStockByIndex(short market, int index, out SkDomesticStock stock);

    /// <summary>
    /// 以市場別與代碼取商品(<c>SKQuoteLib_GetStockByMarketAndNo</c>,手冊 4-4-33;沒訂報價時只有基本資料,夠拿索引與小數位數)。
    /// Reads a domestic stock by market and code; without a quote subscription only the basic data, which is enough for
    /// the index and the decimals.
    /// </summary>
    /// <param name="market">市場別。The market number.</param>
    /// <param name="code">代碼。The code.</param>
    /// <param name="stock">商品。The stock.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int GetDomesticStockByCode(short market, string code, out SkDomesticStock stock);

    // ── 海外五檔 / 十檔(SKOSQuoteLib)。Overseas best 5 and best 10. ──

    /// <summary>海外五檔(<c>OnNotifyBest5NineDigitLONG</c>;九位擴充整數,待實測)。Overseas best 5, nine-digit integers.</summary>
    event Action<SkBook>? OverseasBest5;

    /// <summary>海外十檔(<c>OnNotifyBest10NineDigitLONG</c>,<see cref="RequestMarketDepth"/> 之後;待實測)。Overseas best 10.</summary>
    event Action<SkBook>? OverseasBest10;

    /// <summary>訂海外十檔(<c>SKOSQuoteLib_RequestMarketDepth</c>,頁碼 -1;待實測)。Subscribes the overseas best 10, page -1.</summary>
    /// <param name="stockNo">「交易所,代碼」。"Exchange,code".</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestMarketDepth(string stockNo);
}
