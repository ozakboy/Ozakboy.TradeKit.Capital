using System.Runtime.InteropServices;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

/// <summary>
/// 公告事件接收器:群益規定登入前必須註冊,並把確認值回 -1(手冊 3-2、4-3-e)。公告內容交給 <see cref="MessageReceived"/>,
/// 登入帳號一律不往外傳。
/// Announcement sink. Capital requires it before login and the confirm code must be -1. The login ID is never passed on.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class ReplyEventsSink : ISKReplyLibEvents
{
    /// <summary>收到公告(只有公告內容,不含登入帳號)。An announcement arrived (content only, no login ID).</summary>
    public event Action<string>? MessageReceived;

    /// <inheritdoc/>
    public void OnReplyMessage(string bstrUserID, string bstrMessage, out short sConfirmCode)
    {
        sConfirmCode = -1;
        MessageReceived?.Invoke(bstrMessage ?? string.Empty);
    }
}

/// <summary>
/// 海外報價事件接收器:把 COM 事件原樣轉成 .NET 事件,不做任何解析。
/// Overseas quote sink: forwards COM events as .NET events unchanged, with no parsing.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class OsQuoteEventsSink : ISKOSQuoteLibEvents
{
    /// <summary>連線狀態(nCode, nSocketCode)。</summary>
    public event Action<int, int>? Connected;

    /// <summary>商品清單一筆。</summary>
    public event Action<string>? Product;

    /// <summary>商品明細一筆。</summary>
    public event Action<string>? ProductDetail;

    /// <summary>歷史 K 一筆(商品, 資料)。</summary>
    public event Action<string, string>? KLine;

    /// <summary>主機時間(時, 分, 秒)。</summary>
    public event Action<short, short, short>? ServerTime;

    /// <summary>報價異動(索引)。</summary>
    public event Action<int>? Quote;

    /// <summary>成交(索引, 序號, 日期, 時間, 價格, 量, 是否為訂閱當下回補的歷史成交)。</summary>
    public event Action<int, int, int, int, long, int, bool>? Tick;

    /// <summary>最佳五檔(九位小數;serve 不接,探針用)。</summary>
    public event Action<SkBookEvent>? Best5;

    /// <summary>最佳十檔(九位小數;serve 不接,探針用)。</summary>
    public event Action<SkBookEvent>? Best10;

    /// <inheritdoc/>
    public void OnConnect(int nCode, int nSocketCode) => Connected?.Invoke(nCode, nSocketCode);

    /// <inheritdoc/>
    public void OnNotifyBest5NineDigitLONG(
        int nStockIdx,
        long nBestBid1,
        int nBestBidQty1,
        long nBestBid2,
        int nBestBidQty2,
        long nBestBid3,
        int nBestBidQty3,
        long nBestBid4,
        int nBestBidQty4,
        long nBestBid5,
        int nBestBidQty5,
        long nBestAsk1,
        int nBestAskQty1,
        long nBestAsk2,
        int nBestAskQty2,
        long nBestAsk3,
        int nBestAskQty3,
        long nBestAsk4,
        int nBestAskQty4,
        long nBestAsk5,
        int nBestAskQty5) =>
        Best5?.Invoke(new SkBookEvent(-1, nStockIdx,
        [
            nBestBid1, nBestBidQty1, nBestBid2, nBestBidQty2, nBestBid3, nBestBidQty3, nBestBid4, nBestBidQty4, nBestBid5, nBestBidQty5,
            nBestAsk1, nBestAskQty1, nBestAsk2, nBestAskQty2, nBestAsk3, nBestAskQty3, nBestAsk4, nBestAskQty4, nBestAsk5, nBestAskQty5,
        ]));

    /// <inheritdoc/>
    public void OnNotifyBest10NineDigitLONG(
        int nStockIdx,
        long nBestBid1,
        int nBestBidQty1,
        long nBestBid2,
        int nBestBidQty2,
        long nBestBid3,
        int nBestBidQty3,
        long nBestBid4,
        int nBestBidQty4,
        long nBestBid5,
        int nBestBidQty5,
        long nBestBid6,
        int nBestBidQty6,
        long nBestBid7,
        int nBestBidQty7,
        long nBestBid8,
        int nBestBidQty8,
        long nBestBid9,
        int nBestBidQty9,
        long nBestBid10,
        int nBestBidQty10,
        long nBestAsk1,
        int nBestAskQty1,
        long nBestAsk2,
        int nBestAskQty2,
        long nBestAsk3,
        int nBestAskQty3,
        long nBestAsk4,
        int nBestAskQty4,
        long nBestAsk5,
        int nBestAskQty5,
        long nBestAsk6,
        int nBestAskQty6,
        long nBestAsk7,
        int nBestAskQty7,
        long nBestAsk8,
        int nBestAskQty8,
        long nBestAsk9,
        int nBestAskQty9,
        long nBestAsk10,
        int nBestAskQty10) =>
        Best10?.Invoke(new SkBookEvent(-1, nStockIdx,
        [
            nBestBid1, nBestBidQty1, nBestBid2, nBestBidQty2, nBestBid3, nBestBidQty3, nBestBid4, nBestBidQty4, nBestBid5, nBestBidQty5,
            nBestBid6, nBestBidQty6, nBestBid7, nBestBidQty7, nBestBid8, nBestBidQty8, nBestBid9, nBestBidQty9, nBestBid10, nBestBidQty10,
            nBestAsk1, nBestAskQty1, nBestAsk2, nBestAskQty2, nBestAsk3, nBestAskQty3, nBestAsk4, nBestAskQty4, nBestAsk5, nBestAskQty5,
            nBestAsk6, nBestAskQty6, nBestAsk7, nBestAskQty7, nBestAsk8, nBestAskQty8, nBestAsk9, nBestAskQty9, nBestAsk10, nBestAskQty10,
        ]));

    /// <inheritdoc/>
    public void OnOverseaProducts(string bstrValue) => Product?.Invoke(bstrValue ?? string.Empty);

    /// <inheritdoc/>
    public void OnKLineData(string bstrStockNo, string bstrData) => KLine?.Invoke(bstrStockNo ?? string.Empty, bstrData ?? string.Empty);

    /// <inheritdoc/>
    public void OnNotifyServerTime(short sHour, short sMinute, short sSecond) => ServerTime?.Invoke(sHour, sMinute, sSecond);

    /// <inheritdoc/>
    public void OnOverseaProductsDetail(string bstrValue) => ProductDetail?.Invoke(bstrValue ?? string.Empty);

    /// <inheritdoc/>
    public void OnNotifyQuoteLONG(int nStockIdx) => Quote?.Invoke(nStockIdx);

    /// <inheritdoc/>
    public void OnNotifyTicksNineDigitLONG(int nStockIdx, int nPtr, int nDate, int nTime, long nClose, int nQty) =>
        Tick?.Invoke(nStockIdx, nPtr, nDate, nTime, nClose, nQty, false);

    /// <inheritdoc/>
    public void OnNotifyHistoryTicksNineDigitLONG(int nStockIdx, int nPtr, int nDate, int nTime, long nClose, int nQty) =>
        Tick?.Invoke(nStockIdx, nPtr, nDate, nTime, nClose, nQty, true);
}
