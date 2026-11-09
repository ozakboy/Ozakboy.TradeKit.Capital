using System.Runtime.InteropServices;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

/// <summary>
/// 國內報價事件接收器:把 COM 事件原樣轉成 .NET 事件,不做任何解析。
/// Domestic quote sink: forwards COM events as .NET events unchanged, with no parsing.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class QuoteEventsSink : ISKQuoteLibEvents
{
    /// <summary>連線狀態(nKind, nCode)。</summary>
    public event Action<int, int>? Connection;

    /// <summary>歷史 K 一筆(商品, 資料)。</summary>
    public event Action<string, string>? KLine;

    /// <summary>歷史 K 全部送完(結尾字串)。</summary>
    public event Action<string>? KLineComplete;

    /// <summary>主機時間(時, 分, 秒, 總秒數)。</summary>
    public event Action<short, short, short, int>? ServerTime;

    /// <summary>商品清單一則(市場, 資料)。</summary>
    public event Action<short, string>? StockList;

    /// <summary>報價異動(市場別, 索引)。</summary>
    public event Action<short, int>? Quote;

    /// <summary>成交(含訂閱當下的回補)。</summary>
    public event Action<SkDomesticTickEvent>? Tick;

    /// <summary>最佳五檔。</summary>
    public event Action<SkBookEvent>? Best5;

    /// <summary>即時分 K。</summary>
    public event Action<SkLiveKLineEvent>? LiveKLine;

    /// <inheritdoc/>
    public void OnConnection(int nKind, int nCode) => Connection?.Invoke(nKind, nCode);

    /// <inheritdoc/>
    public void OnNotifyQuoteLONG(short sMarketNo, int nStockIdx) => Quote?.Invoke(sMarketNo, nStockIdx);

    /// <inheritdoc/>
    public void OnNotifyHistoryTicksLONG(
        short sMarketNo,
        int nStockIdx,
        int nPtr,
        int nDate,
        int nTimehms,
        int nTimemillismicros,
        int nBid,
        int nAsk,
        int nClose,
        int nQty,
        int nSimulate) =>
        Tick?.Invoke(new SkDomesticTickEvent(sMarketNo, nStockIdx, nPtr, nDate, nTimehms, nTimemillismicros, nBid, nAsk, nClose, nQty, nSimulate, true));

    /// <inheritdoc/>
    public void OnNotifyTicksLONG(
        short sMarketNo,
        int nStockIdx,
        int nPtr,
        int nDate,
        int nTimehms,
        int nTimemillismicros,
        int nBid,
        int nAsk,
        int nClose,
        int nQty,
        int nSimulate) =>
        Tick?.Invoke(new SkDomesticTickEvent(sMarketNo, nStockIdx, nPtr, nDate, nTimehms, nTimemillismicros, nBid, nAsk, nClose, nQty, nSimulate, false));

    /// <inheritdoc/>
    public void OnNotifyBest5LONG(
        short sMarketNo,
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
        int nExtendBid,
        int nExtendBidQty,
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
        int nExtendAsk,
        int nExtendAskQty,
        int nSimulate) =>
        Best5?.Invoke(new SkBookEvent(sMarketNo, nStockIdx,
        [
            nBestBid1, nBestBidQty1, nBestBid2, nBestBidQty2, nBestBid3, nBestBidQty3, nBestBid4, nBestBidQty4, nBestBid5, nBestBidQty5,
            nExtendBid, nExtendBidQty,
            nBestAsk1, nBestAskQty1, nBestAsk2, nBestAskQty2, nBestAsk3, nBestAskQty3, nBestAsk4, nBestAskQty4, nBestAsk5, nBestAskQty5,
            nExtendAsk, nExtendAskQty, nSimulate,
        ]));

    /// <inheritdoc/>
    public void OnNotifyLiveKLineData(int nType, string bstrStockNo, int nDate, int nTimehm, int nOpen, int nHigh, int nLow, int nClose, int nQty) =>
        LiveKLine?.Invoke(new SkLiveKLineEvent(nType, bstrStockNo ?? string.Empty, nDate, nTimehm, nOpen, nHigh, nLow, nClose, nQty));

    /// <inheritdoc/>
    public void OnNotifyKLineData(string bstrStockNo, string bstrData) => KLine?.Invoke(bstrStockNo ?? string.Empty, bstrData ?? string.Empty);

    /// <inheritdoc/>
    public void OnNotifyServerTime(short sHour, short sMinute, short sSecond, int nTotal) => ServerTime?.Invoke(sHour, sMinute, sSecond, nTotal);

    /// <inheritdoc/>
    public void OnNotifyStockList(short sMarketNo, string bstrStockData) => StockList?.Invoke(sMarketNo, bstrStockData ?? string.Empty);

    /// <inheritdoc/>
    public void OnKLineComplete(string bstrEndString) => KLineComplete?.Invoke(bstrEndString ?? string.Empty);
}
