namespace Ozakboy.TradeKit.Capital.Host.Serve;

// 0.3.0:海外選擇權報價(SKOOQuoteLib)。V2.13.59 手冊沒有這個物件的章節,參數規則比照 SKOSQuoteLib(頁碼 -1、代碼以 # 串接),
// 實測 2026-11-09(probe-full E 段):EnterMonitorLONG 後約 18 秒只來 3001;商品清單每筆一則、以「##,##,##,##,##」結尾;
// 以 # 串接、頁碼 -1 的 RequestStocks 回 0;報價小數位數 2、履約價不放大。斷線代碼 3002 與成交時區未實測。
// 0.3.0: overseas option quotes. The manual has no chapter for this object; the SKOSQuoteLib rules were assumed and
// the connect code, product list and subscription were measured on 2026-11-09.
internal partial interface ISkQuoteSource
{
    /// <summary>
    /// 連線狀態(<c>OnConnect</c> 的 nCode;實測連上是 3001,斷線比照海外推定 3002)。The connection state; 3001 measured, 3002 assumed.
    /// </summary>
    event Action<int>? OverseasOptionConnection;

    /// <summary>商品清單一筆(<c>OnProducts</c>;實測以「##,##,##,##,##」那筆結尾)。One product row; a ## row ends the list, as measured.</summary>
    event Action<string>? OverseasOptionProduct;

    /// <summary>報價異動(<c>OnNotifyQuoteLONG</c>:索引)。A quote changed (index).</summary>
    event Action<int>? OverseasOptionQuote;

    /// <summary>成交(即時或回補;價格原始整數)。A tick, live or replayed; the price is a raw integer.</summary>
    event Action<SkTickData>? OverseasOptionTick;

    /// <summary>五檔(<c>OnNotifyBest5LONG</c>)。The best 5.</summary>
    event Action<SkBook>? OverseasOptionBest5;

    /// <summary>十檔(<c>OnNotifyBest10LONG</c>,RequestMarketDepth 之後)。The best 10.</summary>
    event Action<SkBook>? OverseasOptionBest10;

    /// <summary>
    /// 建立海外選擇權報價物件並掛事件(與國內一樣在登入前建立,probe-full 的順序)。
    /// Creates the overseas option quote object and hooks its events, before login like the domestic one.
    /// </summary>
    /// <returns>成功時為 <see langword="null"/>。Null on success.</returns>
    SkOpenFailure? OpenOverseasOptions();

    /// <summary>連海外選擇權報價主機(<c>SKOOQuoteLib_EnterMonitorLONG</c>)。Connects to the overseas option quote server.</summary>
    /// <returns>群益代碼。Capital's code.</returns>
    int EnterOverseasOptionMonitor();

    /// <summary>離開海外選擇權報價主機(<c>SKOOQuoteLib_LeaveMonitor</c>)。Leaves the overseas option quote server.</summary>
    /// <returns>群益代碼。Capital's code.</returns>
    int LeaveOverseasOptionMonitor();

    /// <summary>要商品清單(<c>SKOOQuoteLib_RequestProducts</c>)。Requests the product list.</summary>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestOverseasOptionProducts();

    /// <summary>訂報價(<c>SKOOQuoteLib_RequestStocks</c>,頁碼 -1、以 # 串接;待實測)。Subscribes quotes; page -1, joined by '#'.</summary>
    /// <param name="stockNos">以 # 串接的「交易所,代碼」。Joined "exchange,code".</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestOverseasOptionStocks(string stockNos);

    /// <summary>訂成交與五檔(<c>SKOOQuoteLib_RequestTicks</c>,頁碼 -1)。Subscribes ticks and the best 5, page -1.</summary>
    /// <param name="stockNo">「交易所,代碼」。"Exchange,code".</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestOverseasOptionTicks(string stockNo);

    /// <summary>訂十檔(<c>SKOOQuoteLib_RequestMarketDepth</c>,頁碼 -1)。Subscribes the best 10, page -1.</summary>
    /// <param name="stockNo">「交易所,代碼」。"Exchange,code".</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int RequestOverseasOptionMarketDepth(string stockNo);

    /// <summary>以索引取報價(<c>SKOOQuoteLib_GetStockByIndexLONG</c>)。Reads a quote by index.</summary>
    /// <param name="index">索引。The index.</param>
    /// <param name="quote">報價。The quote.</param>
    /// <returns>群益代碼。Capital's code.</returns>
    int GetOverseasOptionStockByIndex(int index, out SkOptionQuote quote);
}
