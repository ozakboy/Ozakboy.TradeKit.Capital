using System.Runtime.InteropServices;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

// 此檔由 tools/SkComInteropGen/Generate-SkComInterop.ps1 依 SKCOM 2.13.59 型別庫(群益附的 Interop.SKCOMLib.dll
// 中繼資料)產生,勿手改:要改請改 tools/SkComInteropGen/skcom-interop.psd1 後重新產生。群益的 DLL 不進 repo。
// 雙重介面走 vtable 呼叫,方法順序照型別庫(MetadataToken);用不到的方法是 Unused* 佔位(型別一律 IntPtr,不影響 vtable 位置,
// 絕對不要呼叫);事件介面只宣告要接的事件(沒宣告的 DispId 由 COM 回 DISP_E_MEMBERNOTFOUND,無害)。
// Generated from the SKCOM type library metadata by tools/SkComInteropGen; do not edit by hand. Dual interfaces are
// called through the vtable, so method order follows the type library; unused slots are Unused* placeholders.
// SKQuoteLib(國內證券 / 期貨 / 選擇權報價)。型別庫的 DispId 在第 3 格之後跳過 4,vtable 看的是順序不是 DispId。

/// <summary>SKQuoteLib 的 CLSID 與事件介面 IID。SKQuoteLib class and event interface identifiers.</summary>
internal static class SkComDomesticIds
{
    /// <summary>SKQuoteLib 的 CLSID(SKQuoteLibClass)。</summary>
    public static readonly Guid QuoteLibClsid = new("E7BCB8BB-E1F0-4F6F-A944-2679195E5807");

    /// <summary>_ISKQuoteLibEvents 的 IID。</summary>
    public static readonly Guid QuoteLibEventsIid = new("B6B30C9A-CA64-437B-B415-3BD03004A544");
}

/// <summary>SKQuoteLib:國內證券 / 期權報價。</summary>
[ComImport]
[Guid("9AF518AE-363A-4555-81F2-37D39CD6B7D9")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface ISKQuoteLib
{
    [DispId(1)]
    int UnusedEnterMonitor();

    [DispId(2)]
    int SKQuoteLib_LeaveMonitor();

    /// <summary>訂閱報價(手冊 4-4-2:代碼以逗號分隔、psPageNo 自 V2.13.54 起固定帶 1;一個物件只能有一組即時報價訂閱)。</summary>
    [DispId(3)]
    int SKQuoteLib_RequestStocks(ref short psPageNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockNos);

    [DispId(5)]
    int UnusedGetStockByNo(IntPtr bstrStockNos, IntPtr pSKStock);

    /// <summary>訂閱成交明細、五檔、即時分 K(手冊 4-4-3:psPageNo 從 0 開始,一個 Page 一檔)。</summary>
    [DispId(6)]
    int SKQuoteLib_RequestTicks(ref short psPageNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockNo);

    [DispId(7)]
    int UnusedGetTick(IntPtr sMarketNo, IntPtr sStockIdx, IntPtr nPtr, IntPtr pSKTick);

    [DispId(8)]
    int UnusedGetBest5(IntPtr sMarketNo, IntPtr sStockIdx, IntPtr pSKBest5);

    [DispId(9)]
    int UnusedRequestKLine(IntPtr bstrStockNo, IntPtr sKLineType, IntPtr sOutType);

    [DispId(10)]
    int SKQuoteLib_RequestServerTime();

    [DispId(11)]
    int UnusedGetMarketBuySellUpDown();

    [DispId(12)]
    int UnusedRequestMACD(IntPtr psPageNo, IntPtr bstrStockNo);

    [DispId(13)]
    int UnusedGetMACD(IntPtr sMarketNo, IntPtr sStockIdx, IntPtr pSKMACD);

    [DispId(14)]
    int UnusedRequestBoolTunel(IntPtr psPageNo, IntPtr bstrStockNo);

    [DispId(15)]
    int UnusedGetBoolTunel(IntPtr sMarketNo, IntPtr sStockIdx, IntPtr pSKBoolTunel);

    [DispId(16)]
    int UnusedRequestFutureTradeInfo(IntPtr psPageNo, IntPtr bstrStockNo);

    [DispId(17)]
    int UnusedDelta(IntPtr nCallPut, IntPtr S, IntPtr K, IntPtr R, IntPtr T, IntPtr sigma, IntPtr dDelta);

    [DispId(18)]
    int UnusedGamma(IntPtr S, IntPtr K, IntPtr R, IntPtr T, IntPtr sigma, IntPtr dGamma);

    [DispId(19)]
    int UnusedTheta(IntPtr nCallPut, IntPtr S, IntPtr K, IntPtr R, IntPtr T, IntPtr sigma, IntPtr dTheta);

    [DispId(20)]
    int UnusedVega(IntPtr S, IntPtr K, IntPtr R, IntPtr T, IntPtr sigma, IntPtr dVega);

    [DispId(21)]
    int UnusedRho(IntPtr nCallPut, IntPtr S, IntPtr K, IntPtr R, IntPtr T, IntPtr sigma, IntPtr dRho);

    [DispId(22)]
    int UnusedGetStrikePrices();

    [DispId(23)]
    int UnusedRequestKLineAM(IntPtr bstrStockNo, IntPtr sKLineType, IntPtr sOutType, IntPtr sTradeSession);

    /// <summary>商品清單(手冊 4-4-17:上市 0、上櫃 1、期貨 2、選擇權 3…),以 ## 結尾。</summary>
    [DispId(24)]
    int SKQuoteLib_RequestStockList(short sMarketNo);

    [DispId(25)]
    int UnusedRequestLiveTick(IntPtr psPageNo, IntPtr bstrStockNo);

    [DispId(26)]
    int SKQuoteLib_IsConnected();

    [DispId(27)]
    int UnusedGetMarketPriceTS();

    [DispId(28)]
    int UnusedCancelRequestStocks(IntPtr bstrStockNos);

    [DispId(29)]
    int UnusedCancelRequestTicks(IntPtr bstrStockNo);

    [DispId(30)]
    int SKQuoteLib_GetQuoteStatus(out int pnConnectionCount, [MarshalAs(UnmanagedType.VariantBool)] out bool pbIsOutLimit);

    /// <summary>依日期與盤別查歷史 K 線(手冊 4-4-24)。</summary>
    [DispId(31)]
    int SKQuoteLib_RequestKLineAMByDate(
        [MarshalAs(UnmanagedType.BStr)] string bstrStockNo,
        short sKLineType,
        short sOutType,
        short sTradeSession,
        [MarshalAs(UnmanagedType.BStr)] string bstrStartDate,
        [MarshalAs(UnmanagedType.BStr)] string bstrEndDate,
        short sMinuteNumber);

    [DispId(32)]
    int SKQuoteLib_EnterMonitorLONG();

    /// <summary>依市場別與索引取報價(手冊 4-4-26;不要在 OnNotifyHistoryTicksLONG 裡呼叫)。</summary>
    [DispId(33)]
    int SKQuoteLib_GetStockByIndexLONG(short sMarketNo, int nStockIdx, ref SKSTOCKLONG pSKStockLONG);

    /// <summary>依代碼取報價(手冊 4-4-27;沒訂報價時只有基本資料)。</summary>
    [DispId(34)]
    int SKQuoteLib_GetStockByNoLONG([MarshalAs(UnmanagedType.BStr)] string bstrStockNos, ref SKSTOCKLONG pSKStockLONG);

    [DispId(35)]
    int UnusedGetTickLONG(IntPtr sMarketNo, IntPtr nStockIdx, IntPtr nPtr, IntPtr pSKTick);

    [DispId(36)]
    int UnusedGetBest5LONG(IntPtr sMarketNo, IntPtr nStockIdx, IntPtr pSKBest5);

    [DispId(37)]
    int UnusedGetMACDLONG(IntPtr sMarketNo, IntPtr nStockIdx, IntPtr pSKMACD);

    [DispId(38)]
    int UnusedGetBoolTunelLONG(IntPtr sMarketNo, IntPtr nStockIdx, IntPtr pSKBoolTunel);

    /// <summary>依市場別與代碼取報價(手冊 4-4-33)。</summary>
    [DispId(39)]
    int SKQuoteLib_GetStockByMarketAndNo(
        short sMarketNo,
        [MarshalAs(UnmanagedType.BStr)] string bstrStockNos,
        ref SKSTOCKLONG pSKStockLONG);

    [DispId(40)]
    int UnusedRequestStocksWithMarketNo(IntPtr psPageNo, IntPtr sMarketNo, IntPtr bstrStockNos);

    [DispId(41)]
    int UnusedRequestTicksWithMarketNo(IntPtr psPageNo, IntPtr sMarketNo, IntPtr bstrStockNo);

    [DispId(42)]
    int UnusedExportStockList(IntPtr sMarketNo);

    [DispId(43)]
    int UnusedGetLiveKLineLONG(IntPtr bstrStockNo, IntPtr pSKKLine);

    [DispId(44)]
    int SKQuoteLib_EnterMonitorLONGByMarket(int nMarketType);
}

/// <summary>_ISKQuoteLibEvents:國內報價事件(只宣告用得到的)。</summary>
[ComImport]
[Guid("B6B30C9A-CA64-437B-B415-3BD03004A544")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISKQuoteLibEvents
{
    /// <summary>連線狀態:nKind 3001 連線、3002 斷線、3003 商品基本資料下載完成(手冊 4-4-a)。</summary>
    [DispId(1)]
    void OnConnection(int nKind, int nCode);

    /// <summary>歷史 K 線(每根一則,手冊 4-4-b)。</summary>
    [DispId(6)]
    void OnNotifyKLineData([MarshalAs(UnmanagedType.BStr)] string bstrStockNo, [MarshalAs(UnmanagedType.BStr)] string bstrData);

    /// <summary>報價主機時間。</summary>
    [DispId(7)]
    void OnNotifyServerTime(short sHour, short sMinute, short sSecond, int nTotal);

    /// <summary>商品清單(每個市場分多則,手冊 4-4-h)。</summary>
    [DispId(16)]
    void OnNotifyStockList(short sMarketNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockData);

    /// <summary>報價異動,再以 GetStockByIndexLONG 取值(手冊 4-4-k)。</summary>
    [DispId(19)]
    void OnNotifyQuoteLONG(short sMarketNo, int nStockIdx);

    /// <summary>訂閱當下回補的當日成交(手冊 4-4-l)。</summary>
    [DispId(20)]
    void OnNotifyHistoryTicksLONG(
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
        int nSimulate);

    /// <summary>即時成交(手冊 4-4-m)。</summary>
    [DispId(21)]
    void OnNotifyTicksLONG(
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
        int nSimulate);

    /// <summary>最佳五檔(手冊 4-4-n)。</summary>
    [DispId(22)]
    void OnNotifyBest5LONG(
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
        int nSimulate);

    /// <summary>歷史 K 線全部送完(結尾字串 "##",手冊 4-4-t)。</summary>
    [DispId(28)]
    void OnKLineComplete([MarshalAs(UnmanagedType.BStr)] string bstrEndString);

    /// <summary>即時分 K(手冊 4-4-u;nType 0 清盤、1 分 K 含當日回補、2 該分鐘每筆)。</summary>
    [DispId(29)]
    void OnNotifyLiveKLineData(
        int nType,
        [MarshalAs(UnmanagedType.BStr)] string bstrStockNo,
        int nDate,
        int nTimehm,
        int nOpen,
        int nHigh,
        int nLow,
        int nClose,
        int nQty);
}

/// <summary>國內報價商品物件(LONG 索引)。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
internal struct SKSTOCKLONG
{
    public int nStockIdx;
    public short sDecimal;
    public short sTypeNo;
    [MarshalAs(UnmanagedType.BStr)] public string bstrMarketNo;
    [MarshalAs(UnmanagedType.BStr)] public string bstrStockNo;
    [MarshalAs(UnmanagedType.BStr)] public string bstrStockName;
    [MarshalAs(UnmanagedType.BStr)] public string bstrStockNoSpread;
    public int nOpen;
    public int nHigh;
    public int nLow;
    public int nClose;
    public int nTickQty;
    public int nRef;
    public int nBid;
    public int nBc;
    public int nAsk;
    public int nAc;
    public int nTBc;
    public int nTAc;
    public int nFutureOI;
    public int nTQty;
    public int nYQty;
    public int nUp;
    public int nDown;
    public int nSimulate;
    public int nDayTrade;
    public int nTradingDay;
    public int nTradingLotFlag;
    public int nDealTime;
    public int nOddLotBid;
    public int nOddLotAsk;
    public int nOddLotClose;
    public int nOddLotQty;
}
