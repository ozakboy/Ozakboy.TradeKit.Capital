using System.Runtime.InteropServices;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

// 群益 SKCOM 的最小 COM 定義,手寫而非引用群益附的 Interop.SKCOMLib.dll(那是群益的檔案,不放進公開 repo)。
// GUID、DispId、方法順序與參數封送照 SKCOM 2.13.59 型別庫(以 ReflectionOnly 讀 Interop.SKCOMLib.dll 的中繼資料核對)。
// 雙重介面(TypeLibType 4288 = Dual | Dispatchable | NonExtensible)走 vtable 呼叫,所以方法**順序**必須與型別庫一致;
// 用不到的方法仍要佔位(參數型別不影響 vtable 位置,一律寫成 IntPtr 並取名 Unused*,絕對不要呼叫)。
// Minimal hand-written COM definitions for Capital's SKCOM. GUIDs, DispIds, method order and marshalling follow the
// SKCOM 2.13.59 type library (checked against the metadata of the vendor's Interop.SKCOMLib.dll). The interfaces are
// dual and called through the vtable, so method ORDER must match; unused slots are declared as Unused* placeholders.

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

/// <summary>SKCenterLib:登入與環境設定(型別庫順序 1–15)。</summary>
[ComImport]
[Guid("D61780D3-2239-4FD8-9C64-0E47B2E75464")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface ISKCenterLib
{
    [DispId(1)]
    int SKCenterLib_Login([MarshalAs(UnmanagedType.BStr)] string bstrUserID, [MarshalAs(UnmanagedType.BStr)] string bstrPassword);

    [DispId(2)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string SKCenterLib_GetReturnCodeMessage(int nCode);

    [DispId(3)]
    int SKCenterLib_SetLogPath([MarshalAs(UnmanagedType.BStr)] string bstrPath);

    [DispId(4)]
    int UnusedDebug(IntPtr unused);

    [DispId(5)]
    int UnusedResetServer(IntPtr unused);

    [DispId(6)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string SKCenterLib_GetLastLogInfo();

    [DispId(7)]
    int UnusedSetAuthority(IntPtr unused);

    [DispId(8)]
    int UnusedRequestAgreement(IntPtr unused);

    [DispId(9)]
    int UnusedLoginSetQuote(IntPtr unused);

    [DispId(10)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string SKCenterLib_GetSKAPIVersionAndBit([MarshalAs(UnmanagedType.BStr)] string bstrUserID);
}

/// <summary>
/// _ISKReplyLibEvents:只接 OnReplyMessage。SKReplyLib 物件本身只用來掛這個事件(手冊 3-2:登入前必要),不呼叫它的方法。
/// </summary>
[ComImport]
[Guid("5491941E-0589-47CE-98E9-63DD0EBD6CC5")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISKReplyLibEvents
{
    /// <summary>公告。登入前必須註冊,並把 <paramref name="sConfirmCode"/> 設成 -1(手冊 4-3-e)。</summary>
    [DispId(6)]
    void OnReplyMessage([MarshalAs(UnmanagedType.BStr)] string bstrUserID, [MarshalAs(UnmanagedType.BStr)] string bstrMessage, out short sConfirmCode);
}

/// <summary>SKOSQuoteLib:海外期貨報價(型別庫順序 1–30)。</summary>
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
    int UnusedSetOSQuoteServer(IntPtr unused);

    [DispId(4)]
    int SKOSQuoteLib_RequestTicks(ref short pnPageNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockNo);

    [DispId(5)]
    int SKOSQuoteLib_RequestOverseaProducts();

    [DispId(6)]
    int UnusedRequestKLine(IntPtr unused1, IntPtr unused2);

    [DispId(7)]
    int SKOSQuoteLib_RequestServerTime();

    [DispId(8)]
    int UnusedGetStockByIndex(IntPtr unused1, IntPtr unused2);

    [DispId(9)]
    int SKOSQuoteLib_RequestStocks(ref short pnPageNo, [MarshalAs(UnmanagedType.BStr)] string bstrStockNos);

    [DispId(10)]
    int UnusedGetStockByNo(IntPtr unused1, IntPtr unused2);

    [DispId(11)]
    int UnusedGetTick(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(12)]
    int UnusedGetBest5(IntPtr unused1, IntPtr unused2);

    [DispId(13)]
    int UnusedInitialize();

    [DispId(14)]
    int SKOSQuoteLib_IsConnected();

    [DispId(15)]
    int SKOSQuoteLib_GetOverseaProductDetail(short sType);

    [DispId(16)]
    int UnusedRequestLiveTick(IntPtr unused1, IntPtr unused2);

    [DispId(17)]
    int UnusedGetStockByIndexNineDigit(IntPtr unused1, IntPtr unused2);

    [DispId(18)]
    int UnusedGetStockByNoNineDigit(IntPtr unused1, IntPtr unused2);

    [DispId(19)]
    int UnusedGetTickNineDigit(IntPtr unused1, IntPtr unused2, IntPtr unused3);

    [DispId(20)]
    int UnusedGetBest5NineDigit(IntPtr unused1, IntPtr unused2);

    [DispId(21)]
    int UnusedRequestMarketDepth(IntPtr unused1, IntPtr unused2);

    [DispId(22)]
    int SKOSQuoteLib_GetQuoteStatus(out int pnConnectionCount, [MarshalAs(UnmanagedType.VariantBool)] out bool pbIsOutLimit);

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
    int UnusedGetStockByIndexLONG(IntPtr unused1, IntPtr unused2);

    [DispId(26)]
    int SKOSQuoteLib_GetStockByIndexNineDigitLONG(int nStockIdx, out SKFOREIGN_9LONG pSKForeign_9LONG);

    [DispId(27)]
    int UnusedGetStockByNoLONG(IntPtr unused1, IntPtr unused2);

    [DispId(28)]
    int SKOSQuoteLib_GetStockByNoNineDigitLONG([MarshalAs(UnmanagedType.BStr)] string bstrStockNo, out SKFOREIGN_9LONG pSKForeign_9LONG);
}

/// <summary>_ISKOSQuoteLibEvents:海外報價事件(只宣告用得到的;沒宣告的 DispId 由 COM 回 DISP_E_MEMBERNOTFOUND,無害)。</summary>
[ComImport]
[Guid("8DB2F1B3-20D6-4EC3-9614-CC6154B6FF72")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISKOSQuoteLibEvents
{
    /// <summary>連線狀態:3001 連線、3002 斷線、3003 商品載入完成…(手冊錯誤代碼表)。</summary>
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
}

/// <summary>海外報價商品物件(九位小數、LONG 索引),欄位順序與封送照型別庫(Pack 8)。</summary>
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
