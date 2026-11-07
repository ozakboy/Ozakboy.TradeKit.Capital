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

    /// <inheritdoc/>
    public void OnConnection(int nKind, int nCode) => Connection?.Invoke(nKind, nCode);

    /// <inheritdoc/>
    public void OnNotifyKLineData(string bstrStockNo, string bstrData) => KLine?.Invoke(bstrStockNo ?? string.Empty, bstrData ?? string.Empty);

    /// <inheritdoc/>
    public void OnNotifyServerTime(short sHour, short sMinute, short sSecond, int nTotal) => ServerTime?.Invoke(sHour, sMinute, sSecond, nTotal);

    /// <inheritdoc/>
    public void OnNotifyStockList(short sMarketNo, string bstrStockData) => StockList?.Invoke(sMarketNo, bstrStockData ?? string.Empty);

    /// <inheritdoc/>
    public void OnKLineComplete(string bstrEndString) => KLineComplete?.Invoke(bstrEndString ?? string.Empty);
}
