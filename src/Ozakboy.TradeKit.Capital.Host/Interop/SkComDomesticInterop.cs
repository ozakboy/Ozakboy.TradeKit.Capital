using System.Runtime.InteropServices;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

// 群益 SKCOM 國內報價(SKQuoteLib)的最小 COM 定義,規則同 SkComInterop.cs:
// 方法順序照 SKCOM 2.13.59 型別庫(以 ReflectionOnly 讀 Interop.SKCOMLib.dll 中繼資料,依 MetadataToken 排序核對,共 43 格);
// 型別庫的 DispId 在第 3 格之後跳過 4(第 4 格是 DispId 5),vtable 看的是順序不是 DispId,佔位照格數寫。
// Minimal COM definitions for Capital's domestic quote library (SKQuoteLib). Same rules as SkComInterop.cs: method order
// follows the SKCOM 2.13.59 type library (43 slots, checked against the vendor interop metadata sorted by token).

/// <summary>SKQuoteLib 的 CLSID 與事件介面 IID。SKQuoteLib class and event interface identifiers.</summary>
internal static class SkComDomesticIds
{
    /// <summary>SKQuoteLib 的 CLSID(SKQuoteLibClass)。</summary>
    public static readonly Guid QuoteLibClsid = new("E7BCB8BB-E1F0-4F6F-A944-2679195E5807");

    /// <summary>_ISKQuoteLibEvents 的 IID。</summary>
    public static readonly Guid QuoteLibEventsIid = new("B6B30C9A-CA64-437B-B415-3BD03004A544");
}

/// <summary>SKQuoteLib:國內證券 / 期權報價(型別庫順序 1–43)。</summary>
[ComImport]
[Guid("9AF518AE-363A-4555-81F2-37D39CD6B7D9")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface ISKQuoteLib
{
    [DispId(1)]
    int UnusedEnterMonitor();

    [DispId(2)]
    int SKQuoteLib_LeaveMonitor();

    [DispId(3)]
    int UnusedRequestStocks(IntPtr unused1, IntPtr unused2);

    [DispId(5)]
    int UnusedGetStockByNo(IntPtr unused1, IntPtr unused2);

    [DispId(6)]
    int UnusedRequestTicks(IntPtr unused1, IntPtr unused2);

    [DispId(7)]
    int UnusedGetTick(IntPtr unused1, IntPtr unused2, IntPtr unused3, IntPtr unused4);

    [DispId(8)]
    int UnusedGetBest5(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(9)]
    int UnusedRequestKLine(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(10)]
    int SKQuoteLib_RequestServerTime();

    [DispId(11)]
    int UnusedGetMarketBuySellUpDown();

    [DispId(12)]
    int UnusedRequestMACD(IntPtr unused1, IntPtr unused2);

    [DispId(13)]
    int UnusedGetMACD(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(14)]
    int UnusedRequestBoolTunel(IntPtr unused1, IntPtr unused2);

    [DispId(15)]
    int UnusedGetBoolTunel(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(16)]
    int UnusedRequestFutureTradeInfo(IntPtr unused1, IntPtr unused2);

    [DispId(17)]
    int UnusedDelta(IntPtr u1, IntPtr u2, IntPtr u3, IntPtr u4, IntPtr u5, IntPtr u6, IntPtr u7);

    [DispId(18)]
    int UnusedGamma(IntPtr u1, IntPtr u2, IntPtr u3, IntPtr u4, IntPtr u5, IntPtr u6);

    [DispId(19)]
    int UnusedTheta(IntPtr u1, IntPtr u2, IntPtr u3, IntPtr u4, IntPtr u5, IntPtr u6, IntPtr u7);

    [DispId(20)]
    int UnusedVega(IntPtr u1, IntPtr u2, IntPtr u3, IntPtr u4, IntPtr u5, IntPtr u6);

    [DispId(21)]
    int UnusedRho(IntPtr u1, IntPtr u2, IntPtr u3, IntPtr u4, IntPtr u5, IntPtr u6, IntPtr u7);

    [DispId(22)]
    int UnusedGetStrikePrices();

    [DispId(23)]
    int UnusedRequestKLineAM(IntPtr unused1, IntPtr unused2, IntPtr unused3, IntPtr unused4);

    [DispId(24)]
    int SKQuoteLib_RequestStockList(short sMarketNo);

    [DispId(25)]
    int UnusedRequestLiveTick(IntPtr unused1, IntPtr unused2);

    [DispId(26)]
    int SKQuoteLib_IsConnected();

    [DispId(27)]
    int UnusedGetMarketPriceTS();

    [DispId(28)]
    int UnusedCancelRequestStocks(IntPtr unused);

    [DispId(29)]
    int UnusedCancelRequestTicks(IntPtr unused);

    [DispId(30)]
    int SKQuoteLib_GetQuoteStatus(out int pnConnectionCount, [MarshalAs(UnmanagedType.VariantBool)] out bool pbIsOutLimit);

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

    [DispId(33)]
    int UnusedGetStockByIndexLONG(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(34)]
    int UnusedGetStockByNoLONG(IntPtr unused1, IntPtr unused2);

    [DispId(35)]
    int UnusedGetTickLONG(IntPtr unused1, IntPtr unused2, IntPtr unused3, IntPtr unused4);

    [DispId(36)]
    int UnusedGetBest5LONG(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(37)]
    int UnusedGetMACDLONG(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(38)]
    int UnusedGetBoolTunelLONG(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(39)]
    int UnusedGetStockByMarketAndNo(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(40)]
    int UnusedRequestStocksWithMarketNo(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(41)]
    int UnusedRequestTicksWithMarketNo(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(42)]
    int UnusedExportStockList(IntPtr unused);

    [DispId(43)]
    int UnusedGetLiveKLineLONG(IntPtr unused1, IntPtr unused2);

    [DispId(44)]
    int SKQuoteLib_EnterMonitorLONGByMarket(int nMarketType);
}

/// <summary>_ISKQuoteLibEvents:國內報價事件(只宣告用得到的;沒宣告的 DispId 由 COM 回 DISP_E_MEMBERNOTFOUND,無害)。</summary>
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

    /// <summary>商品清單(每個市場分多則)。</summary>
    [DispId(16)]
    void OnNotifyStockList(short sMarketNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockData);

    /// <summary>歷史 K 線全部送完(結尾字串 "##",手冊 4-4-t)。</summary>
    [DispId(28)]
    void OnKLineComplete([MarshalAs(UnmanagedType.BStr)] string bstrEndString);
}
