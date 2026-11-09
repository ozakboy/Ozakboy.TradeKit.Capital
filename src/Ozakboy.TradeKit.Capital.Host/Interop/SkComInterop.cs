using System.Runtime.InteropServices;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

// 此檔由 tools/SkComInteropGen/Generate-SkComInterop.ps1 依 SKCOM 2.13.59 型別庫(群益附的 Interop.SKCOMLib.dll
// 中繼資料)產生,勿手改:要改請改 tools/SkComInteropGen/skcom-interop.psd1 後重新產生。群益的 DLL 不進 repo。
// 雙重介面走 vtable 呼叫,方法順序照型別庫(MetadataToken);用不到的方法是 Unused* 佔位(型別一律 IntPtr,不影響 vtable 位置,
// 絕對不要呼叫);事件介面只宣告要接的事件(沒宣告的 DispId 由 COM 回 DISP_E_MEMBERNOTFOUND,無害)。
// Generated from the SKCOM type library metadata by tools/SkComInteropGen; do not edit by hand. Dual interfaces are
// called through the vtable, so method order follows the type library; unused slots are Unused* placeholders.
// SKCenterLib(登入、環境)、SKReplyLib 的公告事件、SKOSQuoteLib(海外期貨 / 外匯報價)。

/// <summary>SKCOM 的 CLSID 與事件介面 IID。SKCOM class and event interface identifiers.</summary>
internal static class SkComIds
{
    /// <summary>SKCenterLib 的 CLSID(SKCenterLibClass)。</summary>
    public static readonly Guid CenterLibClsid = new("AC30BAB5-194A-4515-A8D3-6260749F8577");

    /// <summary>SKReplyLib 的 CLSID(SKReplyLibClass)。</summary>
    public static readonly Guid ReplyLibClsid = new("72D98963-03E9-42AB-B997-BB2E5CCE78DD");

    /// <summary>SKOSQuoteLib 的 CLSID(SKOSQuoteLibClass)。</summary>
    public static readonly Guid OsQuoteLibClsid = new("E3CB8A7C-896F-4828-85FC-8975E56BA2C4");

    /// <summary>_ISKReplyLibEvents 的 IID。</summary>
    public static readonly Guid ReplyLibEventsIid = new("5491941E-0589-47CE-98E9-63DD0EBD6CC5");

    /// <summary>_ISKOSQuoteLibEvents 的 IID。</summary>
    public static readonly Guid OsQuoteLibEventsIid = new("8DB2F1B3-20D6-4EC3-9614-CC6154B6FF72");
}

/// <summary>SKCenterLib:登入與環境設定。</summary>
[ComImport]
[Guid("D61780D3-2239-4FD8-9C64-0E47B2E75464")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface ISKCenterLib
{
    /// <summary>登入(手冊 4-1);帳密錯誤不得自動重試,錯太多次會鎖帳號。</summary>
    [DispId(1)]
    int SKCenterLib_Login([MarshalAs(UnmanagedType.BStr)] string bstrUserID, [MarshalAs(UnmanagedType.BStr)] string bstrPassword);

    /// <summary>代碼對應的訊息。</summary>
    [DispId(2)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string SKCenterLib_GetReturnCodeMessage(int nCode);

    /// <summary>SKCOM 自己的紀錄目錄。</summary>
    [DispId(3)]
    int SKCenterLib_SetLogPath([MarshalAs(UnmanagedType.BStr)] string bstrPath);

    [DispId(4)]
    int UnusedDebug(IntPtr bDebug);

    [DispId(5)]
    int UnusedResetServer(IntPtr bstrServer);

    /// <summary>最後一筆紀錄(登入失敗時看原因;可能含帳號,寫出前要遮)。</summary>
    [DispId(6)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string SKCenterLib_GetLastLogInfo();

    [DispId(7)]
    int UnusedSetAuthority(IntPtr nAuthorityFlag);

    [DispId(8)]
    int UnusedRequestAgreement(IntPtr bstrUserID);

    [DispId(9)]
    int UnusedLoginSetQuote(IntPtr bstrUserID, IntPtr bstrPassword, IntPtr bstrSetFlag);

    /// <summary>SKCOM 版本與位元數。</summary>
    [DispId(10)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string SKCenterLib_GetSKAPIVersionAndBit([MarshalAs(UnmanagedType.BStr)] string bstrUserID);

    [DispId(11)]
    int UnusedGenerateKeyCert(IntPtr bstrLogInID, IntPtr bstrCustCertID);

    [DispId(12)]
    int UnusedLoginGW(IntPtr bstrUserID, IntPtr bstrPassword);

    [DispId(13)]
    int UnusedSetICEBrand(IntPtr bstrUserID);

    [DispId(14)]
    int UnusedSetMCBrand(IntPtr bstrUserID);

    [DispId(15)]
    int UnusedSetMCWhiteBrand(IntPtr bstrUserID, IntPtr bWBflag);
}

/// <summary>_ISKReplyLibEvents:只接 OnReplyMessage。SKReplyLib 物件本身只用來掛這個事件(手冊 3-2:登入前必要),不呼叫它的方法。</summary>
[ComImport]
[Guid("5491941E-0589-47CE-98E9-63DD0EBD6CC5")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISKReplyLibEvents
{
    /// <summary>公告。登入前必須註冊,並把 <paramref name="sConfirmCode"/> 設成 -1(手冊 4-3-e)。</summary>
    [DispId(6)]
    void OnReplyMessage(
        [MarshalAs(UnmanagedType.BStr)] string bstrUserID,
        [MarshalAs(UnmanagedType.BStr)] string bstrMessage,
        out short sConfirmCode);
}

/// <summary>SKOSQuoteLib:海外期貨 / 外匯報價。</summary>
[ComImport]
[Guid("1D404642-09F6-428E-BF0F-3235812BC202")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface ISKOSQuoteLib
{
    [DispId(1)]
    int UnusedEnterMonitor();

    [DispId(2)]
    int SKOSQuoteLib_LeaveMonitor();

    [DispId(3)]
    int UnusedSetOSQuoteServer(IntPtr sServer);

    /// <summary>訂閱成交明細(一次一檔,頁碼 -1 由函式庫指定)。</summary>
    [DispId(4)]
    int SKOSQuoteLib_RequestTicks(ref short pnPageNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockNo);

    /// <summary>商品清單,以 ## 結尾(2026-11-06 實測)。</summary>
    [DispId(5)]
    int SKOSQuoteLib_RequestOverseaProducts();

    [DispId(6)]
    int UnusedRequestKLine(IntPtr bstrStockNo, IntPtr sKLineType);

    [DispId(7)]
    int SKOSQuoteLib_RequestServerTime();

    [DispId(8)]
    int UnusedGetStockByIndex(IntPtr sStockIdx, IntPtr pSKForeign);

    /// <summary>訂閱報價(代碼以 # 串接,頁碼 -1;2026-11-06 實測)。</summary>
    [DispId(9)]
    int SKOSQuoteLib_RequestStocks(ref short pnPageNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockNos);

    [DispId(10)]
    int UnusedGetStockByNo(IntPtr bstrStockNo, IntPtr pSKForeign);

    [DispId(11)]
    int UnusedGetTick(IntPtr sStockIdx, IntPtr nPtr, IntPtr pSKTick);

    [DispId(12)]
    int UnusedGetBest5(IntPtr sStockIdx, IntPtr pSKBest5);

    [DispId(13)]
    int UnusedInitialize();

    [DispId(14)]
    int SKOSQuoteLib_IsConnected();

    /// <summary>商品明細(sType 1),以 ## 結尾。</summary>
    [DispId(15)]
    int SKOSQuoteLib_GetOverseaProductDetail(short sType);

    [DispId(16)]
    int UnusedRequestLiveTick(IntPtr pnPageNo, IntPtr bstrStockNo);

    [DispId(17)]
    int UnusedGetStockByIndexNineDigit(IntPtr sStockIdx, IntPtr pSKForeign);

    [DispId(18)]
    int UnusedGetStockByNoNineDigit(IntPtr bstrStockNo, IntPtr pSKForeign);

    [DispId(19)]
    int UnusedGetTickNineDigit(IntPtr sStockIdx, IntPtr nPtr, IntPtr pSKTick);

    [DispId(20)]
    int UnusedGetBest5NineDigit(IntPtr sStockIdx, IntPtr pSKBest5);

    /// <summary>訂閱十檔(OnNotifyBest10NineDigitLONG;格式待 probe-full 實測)。</summary>
    [DispId(21)]
    int SKOSQuoteLib_RequestMarketDepth(ref short pnPageNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockNo);

    [DispId(22)]
    int SKOSQuoteLib_GetQuoteStatus(out int pnConnectionCount, [MarshalAs(UnmanagedType.VariantBool)] out bool pbIsOutLimit);

    /// <summary>依日期查歷史 K 線。</summary>
    [DispId(23)]
    int SKOSQuoteLib_RequestKLineByDate(
        [MarshalAs(UnmanagedType.BStr)] string bstrStockNo,
        short sKLineType,
        [MarshalAs(UnmanagedType.BStr)] string bstrStartDate,
        [MarshalAs(UnmanagedType.BStr)] string bstrEndDate,
        short sMinuteNumber);

    [DispId(24)]
    int SKOSQuoteLib_EnterMonitorLONG();

    [DispId(25)]
    int UnusedGetStockByIndexLONG(IntPtr nStockIdx, IntPtr pSKForeignLONG);

    [DispId(26)]
    int SKOSQuoteLib_GetStockByIndexNineDigitLONG(int nStockIdx, out SKFOREIGN_9LONG pSKForeign_9LONG);

    [DispId(27)]
    int UnusedGetStockByNoLONG(IntPtr bstrStockNo, IntPtr pSKForeignLONG);

    [DispId(28)]
    int SKOSQuoteLib_GetStockByNoNineDigitLONG([MarshalAs(UnmanagedType.BStr)] string bstrStockNo, out SKFOREIGN_9LONG pSKForeign_9LONG);

    [DispId(29)]
    int UnusedGetTickNineDigitLONG(IntPtr nStockIdx, IntPtr nPtr, IntPtr pSKTick);

    [DispId(30)]
    int UnusedGetBest5NineDigitLONG(IntPtr nStockIdx, IntPtr pSKBest5);
}

/// <summary>_ISKOSQuoteLibEvents:海外報價事件(只宣告用得到的)。</summary>
[ComImport]
[Guid("8DB2F1B3-20D6-4EC3-9614-CC6154B6FF72")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISKOSQuoteLibEvents
{
    /// <summary>連線狀態:3001 連線、3002 斷線(實測只有 3001,沒有 3003)。</summary>
    [DispId(1)]
    void OnConnect(int nCode, int nSocketCode);

    /// <summary>海外商品清單(每筆一則)。</summary>
    [DispId(5)]
    void OnOverseaProducts([MarshalAs(UnmanagedType.BStr)] string bstrValue);

    /// <summary>歷史 K 線(每根一則)。</summary>
    [DispId(6)]
    void OnKLineData([MarshalAs(UnmanagedType.BStr)] string bstrStockNo, [MarshalAs(UnmanagedType.BStr)] string bstrData);

    /// <summary>報價主機時間。</summary>
    [DispId(7)]
    void OnNotifyServerTime(short sHour, short sMinute, short sSecond);

    /// <summary>海外商品明細(含商品種類)。</summary>
    [DispId(9)]
    void OnOverseaProductsDetail([MarshalAs(UnmanagedType.BStr)] string bstrValue);

    /// <summary>報價異動,再以 GetStockByIndexNineDigitLONG 取值。</summary>
    [DispId(15)]
    void OnNotifyQuoteLONG(int nStockIdx);

    /// <summary>即時成交(九位小數)。</summary>
    [DispId(16)]
    void OnNotifyTicksNineDigitLONG(int nStockIdx, int nPtr, int nDate, int nTime, long nClose, int nQty);

    /// <summary>訂閱當下回補的當日歷史成交(九位小數)。</summary>
    [DispId(17)]
    void OnNotifyHistoryTicksNineDigitLONG(int nStockIdx, int nPtr, int nDate, int nTime, long nClose, int nQty);

    /// <summary>最佳五檔(九位小數;格式待 probe-full 實測)。</summary>
    [DispId(18)]
    void OnNotifyBest5NineDigitLONG(
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
        int nBestAskQty5);

    /// <summary>最佳十檔(RequestMarketDepth;格式待 probe-full 實測)。</summary>
    [DispId(19)]
    void OnNotifyBest10NineDigitLONG(
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
        int nBestAskQty10);
}

/// <summary>海外報價商品物件(九位小數、LONG 索引)。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
internal struct SKFOREIGN_9LONG
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
    public long nOpen;
    public long nHigh;
    public long nLow;
    public long nClose;
    public long nSettlePrice;
    public int nTickQty;
    public long nRef;
    public long nBid;
    public int nBc;
    public long nAsk;
    public int nAc;
    public long nTQty;
    public int nStrikePrice;
    public int nTradingDay;
}
