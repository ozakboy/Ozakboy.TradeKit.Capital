using System.Runtime.InteropServices;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

/// <summary>
/// 海外選擇權報價事件接收器:把 COM 事件原樣轉成 .NET 事件,不做任何解析。
/// Overseas option quote sink: forwards COM events as .NET events unchanged, with no parsing.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class OoQuoteEventsSink : ISKOOQuoteLibEvents
{
    /// <summary>連線狀態(nCode, nSocketCode)。</summary>
    public event Action<int, int>? Connected;

    /// <summary>商品清單一筆。</summary>
    public event Action<string>? Product;

    /// <summary>報價異動(索引)。</summary>
    public event Action<int>? Quote;

    /// <summary>成交(含訂閱當下的回補)。</summary>
    public event Action<SkForeignTickEvent>? Tick;

    /// <summary>最佳五檔。</summary>
    public event Action<SkBookEvent>? Best5;

    /// <summary>最佳十檔。</summary>
    public event Action<SkBookEvent>? Best10;

    /// <inheritdoc/>
    public void OnConnect(int nCode, int nSocketCode) => Connected?.Invoke(nCode, nSocketCode);

    /// <inheritdoc/>
    public void OnProducts(string bstrValue) => Product?.Invoke(bstrValue ?? string.Empty);

    /// <inheritdoc/>
    public void OnNotifyQuoteLONG(int nStockIdx) => Quote?.Invoke(nStockIdx);

    /// <inheritdoc/>
    public void OnNotifyTicksLONG(int nStockIdx, int nPtr, int nDate, int nTime, int nClose, int nQty) =>
        Tick?.Invoke(new SkForeignTickEvent(nStockIdx, nPtr, nDate, nTime, nClose, nQty, false));

    /// <inheritdoc/>
    public void OnNotifyHistoryTicksLONG(int nStockIdx, int nPtr, int nDate, int nTime, int nClose, int nQty) =>
        Tick?.Invoke(new SkForeignTickEvent(nStockIdx, nPtr, nDate, nTime, nClose, nQty, true));

    /// <inheritdoc/>
    public void OnNotifyBest5LONG(
        int nStockIdx,
        int nBestBid1,
        int nBestBidQty1,
        int nBestBid2,
        int nBestBidQty2,
        int nBestBid3,
        int nBestBidQty3,
        int nBestBid4,
        int nBestBidQty4,
        int nBestBid5,
        int nBestBidQty5,
        int nBestAsk1,
        int nBestAskQty1,
        int nBestAsk2,
        int nBestAskQty2,
        int nBestAsk3,
        int nBestAskQty3,
        int nBestAsk4,
        int nBestAskQty4,
        int nBestAsk5,
        int nBestAskQty5) =>
        Best5?.Invoke(new SkBookEvent(-1, nStockIdx,
        [
            nBestBid1, nBestBidQty1, nBestBid2, nBestBidQty2, nBestBid3, nBestBidQty3, nBestBid4, nBestBidQty4, nBestBid5, nBestBidQty5,
            nBestAsk1, nBestAskQty1, nBestAsk2, nBestAskQty2, nBestAsk3, nBestAskQty3, nBestAsk4, nBestAskQty4, nBestAsk5, nBestAskQty5,
        ]));

    /// <inheritdoc/>
    public void OnNotifyBest10LONG(
        int nStockIdx,
        int nBestBid1,
        int nBestBidQty1,
        int nBestBid2,
        int nBestBidQty2,
        int nBestBid3,
        int nBestBidQty3,
        int nBestBid4,
        int nBestBidQty4,
        int nBestBid5,
        int nBestBidQty5,
        int nBestBid6,
        int nBestBidQty6,
        int nBestBid7,
        int nBestBidQty7,
        int nBestBid8,
        int nBestBidQty8,
        int nBestBid9,
        int nBestBidQty9,
        int nBestBid10,
        int nBestBidQty10,
        int nBestAsk1,
        int nBestAskQty1,
        int nBestAsk2,
        int nBestAskQty2,
        int nBestAsk3,
        int nBestAskQty3,
        int nBestAsk4,
        int nBestAskQty4,
        int nBestAsk5,
        int nBestAskQty5,
        int nBestAsk6,
        int nBestAskQty6,
        int nBestAsk7,
        int nBestAskQty7,
        int nBestAsk8,
        int nBestAskQty8,
        int nBestAsk9,
        int nBestAskQty9,
        int nBestAsk10,
        int nBestAskQty10) =>
        Best10?.Invoke(new SkBookEvent(-1, nStockIdx,
        [
            nBestBid1, nBestBidQty1, nBestBid2, nBestBidQty2, nBestBid3, nBestBidQty3, nBestBid4, nBestBidQty4, nBestBid5, nBestBidQty5,
            nBestBid6, nBestBidQty6, nBestBid7, nBestBidQty7, nBestBid8, nBestBidQty8, nBestBid9, nBestBidQty9, nBestBid10, nBestBidQty10,
            nBestAsk1, nBestAskQty1, nBestAsk2, nBestAskQty2, nBestAsk3, nBestAskQty3, nBestAsk4, nBestAskQty4, nBestAsk5, nBestAskQty5,
            nBestAsk6, nBestAskQty6, nBestAsk7, nBestAskQty7, nBestAsk8, nBestAskQty8, nBestAsk9, nBestAskQty9, nBestAsk10, nBestAskQty10,
        ]));
}
