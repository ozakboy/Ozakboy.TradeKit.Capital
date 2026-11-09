using System.Runtime.InteropServices;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

// 此檔由 tools/SkComInteropGen/Generate-SkComInterop.ps1 依 SKCOM 2.13.59 型別庫(群益附的 Interop.SKCOMLib.dll
// 中繼資料)產生,勿手改:要改請改 tools/SkComInteropGen/skcom-interop.psd1 後重新產生。群益的 DLL 不進 repo。
// 雙重介面走 vtable 呼叫,方法順序照型別庫(MetadataToken);用不到的方法是 Unused* 佔位(型別一律 IntPtr,不影響 vtable 位置,
// 絕對不要呼叫);事件介面只宣告要接的事件(沒宣告的 DispId 由 COM 回 DISP_E_MEMBERNOTFOUND,無害)。
// Generated from the SKCOM type library metadata by tools/SkComInteropGen; do not edit by hand. Dual interfaces are
// called through the vtable, so method order follows the type library; unused slots are Unused* placeholders.
// SKOOQuoteLib(海外選擇權報價)。V2.13.59 手冊沒有這個物件的章節,參數規則比照 SKOSQuoteLib(頁碼 -1、代碼以 # 串接),待 probe-full 實測。

/// <summary>SKOOQuoteLib 的 CLSID 與事件介面 IID。SKOOQuoteLib class and event interface identifiers.</summary>
internal static class SkComOverseasOptionIds
{
    /// <summary>SKOOQuoteLib 的 CLSID(SKOOQuoteLibClass)。</summary>
    public static readonly Guid OoQuoteLibClsid = new("853EC706-F437-46E2-80E0-896901A5B490");

    /// <summary>_ISKOOQuoteLibEvents 的 IID。</summary>
    public static readonly Guid OoQuoteLibEventsIid = new("A92F5D7E-6C63-4937-8569-17CD96A5128F");
}

/// <summary>SKOOQuoteLib:海外選擇權報價。</summary>
[ComImport]
[Guid("8B818A3E-7B33-4294-AD44-1890BD358148")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface ISKOOQuoteLib
{
    [DispId(1)]
    int UnusedEnterMonitor();

    [DispId(2)]
    int SKOOQuoteLib_LeaveMonitor();

    /// <summary>商品清單,由 OnProducts 回傳(結尾標記待實測)。</summary>
    [DispId(3)]
    int SKOOQuoteLib_RequestProducts();

    /// <summary>訂閱報價。</summary>
    [DispId(4)]
    int SKOOQuoteLib_RequestStocks(ref short pnPageNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockNos);

    [DispId(5)]
    int UnusedGetStockByIndex(IntPtr sStockIdx, IntPtr pSKForeign);

    [DispId(6)]
    int UnusedGetStockByNo(IntPtr bstrStockNo, IntPtr pSKForeign);

    /// <summary>訂閱成交明細與五檔。</summary>
    [DispId(7)]
    int SKOOQuoteLib_RequestTicks(ref short pnPageNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockNo);

    [DispId(8)]
    int UnusedGetTick(IntPtr sStockIdx, IntPtr nPtr, IntPtr pSKTick);

    [DispId(9)]
    int UnusedGetBest5(IntPtr sStockIdx, IntPtr pSKBest5);

    [DispId(10)]
    int UnusedSetQuoteServer(IntPtr sServer);

    [DispId(11)]
    int SKOOQuoteLib_IsConnected();

    [DispId(12)]
    int UnusedRequestLiveTick(IntPtr pnPageNo, IntPtr bstrStockNo);

    /// <summary>訂閱十檔。</summary>
    [DispId(13)]
    int SKOOQuoteLib_RequestMarketDepth(ref short pnPageNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockNo);

    [DispId(14)]
    int SKOOQuoteLib_EnterMonitorLONG();

    [DispId(15)]
    int SKOOQuoteLib_GetStockByIndexLONG(int nStockIdx, ref SKFOREIGNLONG pSKForeignLONG);

    [DispId(16)]
    int SKOOQuoteLib_GetStockByNoLONG([MarshalAs(UnmanagedType.BStr)] string bstrStockNo, ref SKFOREIGNLONG pSKForeignLONG);

    [DispId(17)]
    int UnusedGetTickLONG(IntPtr nStockIdx, IntPtr nPtr, IntPtr pSKTick);

    [DispId(18)]
    int UnusedGetBest5LONG(IntPtr nStockIdx, IntPtr pSKBest5);
}

/// <summary>_ISKOOQuoteLibEvents:海外選擇權報價事件(只宣告用得到的)。</summary>
[ComImport]
[Guid("A92F5D7E-6C63-4937-8569-17CD96A5128F")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISKOOQuoteLibEvents
{
    /// <summary>連線狀態(代碼待實測)。</summary>
    [DispId(1)]
    void OnConnect(int nCode, int nSocketCode);

    /// <summary>商品清單一筆。</summary>
    [DispId(2)]
    void OnProducts([MarshalAs(UnmanagedType.BStr)] string bstrValue);

    /// <summary>報價異動,再以 GetStockByIndexLONG 取值。</summary>
    [DispId(8)]
    void OnNotifyQuoteLONG(int nStockIdx);

    /// <summary>即時成交。</summary>
    [DispId(9)]
    void OnNotifyTicksLONG(int nStockIdx, int nPtr, int nDate, int nTime, int nClose, int nQty);

    /// <summary>訂閱當下回補的當日成交。</summary>
    [DispId(10)]
    void OnNotifyHistoryTicksLONG(int nStockIdx, int nPtr, int nDate, int nTime, int nClose, int nQty);

    /// <summary>最佳五檔。</summary>
    [DispId(11)]
    void OnNotifyBest5LONG(
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
        int nBestAskQty5);

    /// <summary>最佳十檔(RequestMarketDepth)。</summary>
    [DispId(12)]
    void OnNotifyBest10LONG(
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
        int nBestAskQty10);
}

/// <summary>海外選擇權報價商品物件(LONG 索引)。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
internal struct SKFOREIGNLONG
{
    public int nStockIdx;
    public short sDecimal;
    public int nDenominator;
    [MarshalAs(UnmanagedType.BStr)] public string bstrMarketNo;
    [MarshalAs(UnmanagedType.BStr)] public string bstrExchangeNo;
    [MarshalAs(UnmanagedType.BStr)] public string bstrExchangeName;
    [MarshalAs(UnmanagedType.BStr)] public string bstrStockNo;
    [MarshalAs(UnmanagedType.BStr)] public string bstrStockName;
    [MarshalAs(UnmanagedType.BStr)] public string bstrCallPut;
    public int nOpen;
    public int nHigh;
    public int nLow;
    public int nClose;
    public int nSettlePrice;
    public int nTickQty;
    public int nRef;
    public int nBid;
    public int nBc;
    public int nAsk;
    public int nAc;
    public int nTQty;
    public int nStrikePrice;
    public int nTradingDay;
}
