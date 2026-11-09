using System.Runtime.InteropServices;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

// 此檔由 tools/SkComInteropGen/Generate-SkComInterop.ps1 依 SKCOM 2.13.59 型別庫(群益附的 Interop.SKCOMLib.dll
// 中繼資料)產生,勿手改:要改請改 tools/SkComInteropGen/skcom-interop.psd1 後重新產生。群益的 DLL 不進 repo。
// 雙重介面走 vtable 呼叫,方法順序照型別庫(MetadataToken);用不到的方法是 Unused* 佔位(型別一律 IntPtr,不影響 vtable 位置,
// 絕對不要呼叫);事件介面只宣告要接的事件(沒宣告的 DispId 由 COM 回 DISP_E_MEMBERNOTFOUND,無害)。
// Generated from the SKCOM type library metadata by tools/SkComInteropGen; do not edit by hand. Dual interfaces are
// called through the vtable, so method order follows the type library; unused slots are Unused* placeholders.
// SKOrderLib(帳務查詢)。
// 安全規則:這個套件只讀(報價與帳務查詢),而且群益已經沒有模擬平台,呼叫錯一個方法就是真單、真金流。
// 所以 ISKOrderLib 上所有會改變委託、部位或資金的方法(送單、刪單、減量、改價、停損 / MIT / OCO / 智慧單、代理下單、
// WithDraw、CapitalPayWithDraw、SpecialRequest、UnlockOrder、SetMaxQty、SetMaxCount…)一律只能是 Unused 佔位,
// 不得有可呼叫的真簽章。產生器對 Forbidden 字樣硬擋,單元測試 SkComOrderInteropSafetyTests 以反射再擋一次。
// Safety rule: this package is read-only and Capital has no simulation platform any more, so every method that could
// change an order, a position or funds stays an Unused placeholder. The generator and a reflection test both enforce it.

/// <summary>SKOrderLib 的 CLSID 與事件介面 IID。SKOrderLib class and event interface identifiers.</summary>
internal static class SkComOrderIds
{
    /// <summary>SKOrderLib 的 CLSID(SKOrderLibClass)。</summary>
    public static readonly Guid OrderLibClsid = new("54FE0E28-89B6-43A7-9F07-BE988BB40299");

    /// <summary>_ISKOrderLibEvents 的 IID。</summary>
    public static readonly Guid OrderLibEventsIid = new("4E81123D-980C-419E-889C-184ED17DC4D6");
}

/// <summary>SKOrderLib:只開放帳務查詢;所有下單 / 刪改單 / 出入金方法都是 Unused 佔位(見檔頭安全規則)。</summary>
[ComImport]
[Guid("7ED1A0E0-CF48-4A31-8818-961C84AF48FB")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface ISKOrderLib
{
    /// <summary>下單物件初始化(手冊 4-2-1;登入後、查詢前)。</summary>
    [DispId(1)]
    int SKOrderLib_Initialize();

    /// <summary>取回可交易帳號,由 OnAccount 回傳(手冊 4-2-2)。</summary>
    [DispId(2)]
    int GetUserAccount();

    /// <summary>讀取憑證(手冊 4-2-3)。</summary>
    [DispId(3)]
    int ReadCertByID([MarshalAs(UnmanagedType.BStr)] string bstrLogInID);

    [DispId(4)]
    int UnusedSetMaxQty(IntPtr nMarketType, IntPtr nMaxQty);

    [DispId(5)]
    int UnusedSetMaxCount(IntPtr nMarketType, IntPtr nMaxCount);

    [DispId(6)]
    int UnusedSendStockOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(7)]
    int UnusedUnlockOrder(IntPtr nMarketType);

    [DispId(8)]
    int UnusedSendFutureOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(9)]
    int UnusedSendOptionOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(10)]
    int UnusedSendOverseaFutureOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(11)]
    int UnusedSendOverseaOptionOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(12)]
    int UnusedSendFutureStopLossOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(13)]
    int UnusedSendMovingStopLossOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(14)]
    int UnusedSendOptionStopLossOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(15)]
    int UnusedDecreaseOrderBySeqNo(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSeqNo,
        IntPtr nDecreaseQty,
        IntPtr bstrMessage);

    [DispId(16)]
    int UnusedOverSeaDecreaseOrderBySeqNo(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSeqNo,
        IntPtr nDecreaseQty,
        IntPtr bstrMessage);

    [DispId(17)]
    int UnusedCancelOrderBySeqNo(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr bstrAccount, IntPtr bstrSeqNo, IntPtr bstrMessage);

    [DispId(18)]
    int UnusedCancelOrderByStockNo(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr bstrAccount, IntPtr bstrStockNo, IntPtr bstrMessage);

    [DispId(19)]
    int UnusedCancelFutureStopLoss(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSmartKey,
        IntPtr bstrTradeKind,
        IntPtr bstrMessage);

    [DispId(20)]
    int UnusedCancelMovingStopLoss(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSmartKey,
        IntPtr bstrTradeKind,
        IntPtr bstrMessage);

    [DispId(21)]
    int UnusedCancelOptionStopLoss(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSmartKey,
        IntPtr bstrTradeKind,
        IntPtr bstrMessage);

    [DispId(22)]
    int UnusedCorrectPriceBySeqNo(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSeqNo,
        IntPtr bstrPrice,
        IntPtr nTradeType,
        IntPtr bstrMessage);

    [DispId(23)]
    int UnusedSendOverseaFutureSpreadOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(24)]
    int UnusedGetRealBalanceReport(IntPtr bstrLogInID, IntPtr bstrAccount);

    [DispId(25)]
    int UnusedGetOpenInterest(IntPtr bstrLogInID, IntPtr bstrAccount);

    [DispId(26)]
    int UnusedGetOverseaFutureOpenInterest(IntPtr bstrLogInID, IntPtr bstrAccount);

    [DispId(27)]
    int UnusedGetStopLossReport(IntPtr bstrLogInID, IntPtr bstrAccount, IntPtr nReportStatus, IntPtr bstrKind, IntPtr bstrDate);

    [DispId(28)]
    int UnusedGetOverseaFutures();

    [DispId(29)]
    int UnusedGetOverseaOptions();

    [DispId(30)]
    int UnusedOverSeaCancelOrderBySeqNo(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr bstrAccount, IntPtr bstrSeqNo, IntPtr bstrMessage);

    [DispId(31)]
    int UnusedGetLoginType(IntPtr bstrLogInID);

    [DispId(32)]
    int UnusedGetSpeedyType(IntPtr bstrLogInID);

    [DispId(33)]
    int UnusedLoadOSCommodity();

    [DispId(34)]
    int UnusedLoadOOCommodity();

    /// <summary>國內期貨權益數(手冊 4-2-41;sCoinType 0 全幣別、1 基幣台幣、2 人民幣),由 OnFutureRights / OnFutureRightsStatus 回傳。</summary>
    [DispId(35)]
    int GetFutureRights(
        [MarshalAs(UnmanagedType.BStr)] string bstrLogInID,
        [MarshalAs(UnmanagedType.BStr)] string bstrAccount,
        short sCoinType);

    [DispId(37)]
    int UnusedOverSeaCancelOrderByBookNo(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr bstrAccount, IntPtr bstrBookNo, IntPtr bstrMessage);

    [DispId(38)]
    int UnusedCorrectPriceByBookNo(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrMarketSymbol,
        IntPtr bstrBookNo,
        IntPtr bstrPrice,
        IntPtr nTradeType,
        IntPtr bstrMessage);

    [DispId(39)]
    int UnusedSendFutureOrderCLR(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(40)]
    int UnusedSendForeignStockOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(41)]
    int UnusedAlterOverSeaFutureOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(42)]
    int UnusedBasketOption(IntPtr bstrLogInID, IntPtr nFunc, IntPtr pAsyncOrder, IntPtr bstrMessage, IntPtr bAsyncOrder);

    [DispId(43)]
    int UnusedGetRequestProfitReport(IntPtr bstrLogInID, IntPtr bstrAccount);

    [DispId(44)]
    int UnusedSendFutureOCOOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(45)]
    int UnusedCancelFutureOCO(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSmartKey,
        IntPtr bstrTradeKind,
        IntPtr bstrMessage);

    /// <summary>海外期貨權益數(手冊 4-2-45),由 OnOverSeaFutureRight / OnOverseaFutureRightsStatus 回傳。</summary>
    [DispId(46)]
    int GetRequestOverSeaFutureRight(
        [MarshalAs(UnmanagedType.BStr)] string bstrLogInID,
        [MarshalAs(UnmanagedType.BStr)] string bstrAccount);

    [DispId(47)]
    int UnusedGetMarginPurchaseAmountLimit(IntPtr bstrLogInID, IntPtr bstrAccount, IntPtr bstrStockNo);

    [DispId(48)]
    int UnusedGetBalanceQuery(IntPtr bstrLogInID, IntPtr bstrAccount, IntPtr bstrStockNo);

    [DispId(49)]
    int UnusedSendFutureMITOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(50)]
    int UnusedSendOptionMITOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(51)]
    int UnusedCancelFutureMIT(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSmartKey,
        IntPtr bstrTradeKind,
        IntPtr bstrMessage);

    [DispId(52)]
    int UnusedCancelOptionMIT(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSmartKey,
        IntPtr bstrTradeKind,
        IntPtr bstrMessage);

    [DispId(53)]
    int UnusedSendDuplexOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(54)]
    int UnusedAddSGXAPIOrderSocket(IntPtr bstrLogInID, IntPtr bstrAccount);

    [DispId(55)]
    int UnusedCancelOrderByBookNo(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr bstrAccount, IntPtr bstrBookNo, IntPtr bstrMessage);

    [DispId(56)]
    int UnusedSendTXOffset(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrYearMonth,
        IntPtr nBuySell,
        IntPtr nQty,
        IntPtr bstrMessage);

    [DispId(57)]
    int UnusedAssembleOptions(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(58)]
    int UnusedDisassembleOptions(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(59)]
    int UnusedCoverAllProduct(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(60)]
    int UnusedWithDraw(
        IntPtr bstrLogInID,
        IntPtr bstrFullAccountOut,
        IntPtr nTypeOut,
        IntPtr bstrFullAccountIn,
        IntPtr nTypeIn,
        IntPtr nCurrency,
        IntPtr bstrDollars,
        IntPtr bstrPassword,
        IntPtr bstrMessage);

    [DispId(61)]
    int UnusedGetOpenInterestWithFormat(IntPtr bstrLogInID, IntPtr bstrAccount, IntPtr nFormat);

    [DispId(62)]
    int UnusedSendStockStrategyDayTrade(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(63)]
    int UnusedSendStockStrategyClear(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(64)]
    int UnusedGetTSSmartStrategyReport(
        IntPtr bstrLogInID,
        IntPtr bstrAccount,
        IntPtr bstrMarketType,
        IntPtr nReportStatus,
        IntPtr bstrKind,
        IntPtr bstrDate);

    [DispId(65)]
    int UnusedCancelTSStrategyOrder(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSmartKey,
        IntPtr nTradeKind,
        IntPtr bstrMessage);

    [DispId(66)]
    int UnusedOverSeaCorrectPriceByBookNo(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(67)]
    int UnusedOverSeaCorrectPriceSpreadByBookNo(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(68)]
    int UnusedOverSeaOptionCorrectPriceByBookNo(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(69)]
    int UnusedSendOverseaFutureOrderOLID(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr pAsyncOrder,
        IntPtr bstrOrderLinkedID,
        IntPtr bstrMessage);

    [DispId(70)]
    int UnusedSendOverseaFutureSpreadOrderOLID(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr pAsyncOrder,
        IntPtr bstrOrderLinkedID,
        IntPtr bstrMessage);

    [DispId(71)]
    int UnusedOverSeaCorrectPriceBySGXAPISeqNo(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSeqNo,
        IntPtr bstrCorrectPrice,
        IntPtr bstrMessage);

    [DispId(72)]
    int UnusedSendStockStrategyMIT(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(73)]
    int UnusedSendStockStrategyOCO(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(74)]
    int UnusedGetProfitLossGWReport(IntPtr bstrLogInID, IntPtr pPLGWQuery);

    /// <summary>海外期貨未平倉(手冊 4-2-76;nFormat 1 彙總、2 明細),由 OnOFOpenInterestGWReport / OnOverseaFutureOpenInterestGWStatus 回傳。</summary>
    [DispId(75)]
    int GetOverseaFutureOpenInterestGW(
        [MarshalAs(UnmanagedType.BStr)] string bstrLogInID,
        [MarshalAs(UnmanagedType.BStr)] string bstrAccount,
        int nFormat);

    [DispId(76)]
    int UnusedCancelTFStrategyOrder(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSmartKey,
        IntPtr bstrSymbol,
        IntPtr bstrMessage);

    [DispId(77)]
    int UnusedSendStockStrategyMIOC(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(78)]
    int UnusedSendStockStrategyMST(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(79)]
    int UnusedPingandTracertTest();

    [DispId(80)]
    int UnusedSendStockOddLotOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(81)]
    int UnusedSendTFOffset(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr nCommodity,
        IntPtr bstrYearMonth,
        IntPtr nBuySell,
        IntPtr nQty,
        IntPtr bstrMessage);

    [DispId(82)]
    int UnusedTelnetTest();

    [DispId(83)]
    int UnusedLogUpload();

    [DispId(84)]
    int UnusedCancelTSStrategyOrderV1(IntPtr bstrLogInID, IntPtr pCancelOrder, IntPtr bstrMessage);

    [DispId(85)]
    int UnusedSendFutureSTPOrderV1(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(86)]
    int UnusedSendFutureMSTOrderV1(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(87)]
    int UnusedSendFutureMITOrderV1(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(88)]
    int UnusedSendFutureOCOOrderV1(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(89)]
    int UnusedCancelTFStrategyOrderV1(IntPtr pCancelOrder, IntPtr bAsyncOrder, IntPtr bstrMessage);

    [DispId(90)]
    int UnusedCancelForeignStockOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    /// <summary>國內期貨未平倉(手冊 4-2-89;nFormat 手冊只列 1),由 OnOpenInterest / OnOpenInterestJson / OnOpenInterestGWStatus 回傳。</summary>
    [DispId(91)]
    int GetOpenInterestGW(
        [MarshalAs(UnmanagedType.BStr)] string bstrLogInID,
        [MarshalAs(UnmanagedType.BStr)] string bstrAccount,
        int nFormat);

    [DispId(92)]
    int UnusedDecreaseOrderByBookNo(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr nMarketType,
        IntPtr bstrBookNo,
        IntPtr nDecreaseQty,
        IntPtr bstrMessage);

    [DispId(93)]
    int UnusedMCInitialize(IntPtr bstrLogInID);

    [DispId(94)]
    int UnusedLoadOfCommodityGW(IntPtr bstrLogInID);

    [DispId(95)]
    int UnusedInitialProxyByID(IntPtr bstrLogInID);

    [DispId(96)]
    int UnusedProxyDisconnectByID(IntPtr bstrLogInID);

    [DispId(97)]
    int UnusedProxyReconnectByID(IntPtr bstrLogInID);

    [DispId(98)]
    int UnusedSendOverseaFutureProxyOrder(IntPtr bstrLogInID, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(99)]
    int UnusedSendStockProxyOrder(IntPtr bstrLogInID, IntPtr pSTOCKPROXYORDER, IntPtr bstrMessage);

    [DispId(100)]
    int UnusedSendStockProxyAlter(IntPtr bstrLogInID, IntPtr pSTOCKPROXYORDER, IntPtr bstrMessage);

    [DispId(101)]
    int UnusedSendFutureProxyOrderCLR(IntPtr bstrLogInID, IntPtr pFUTUREPROXYORDER, IntPtr bstrMessage);

    [DispId(102)]
    int UnusedSendFutureProxyAlter(IntPtr bstrLogInID, IntPtr pFUTUREPROXYORDER, IntPtr bstrMessage);

    [DispId(103)]
    int UnusedSendForeignStockProxyOrder(IntPtr bstrLogInID, IntPtr pOSSTOCKPROXYORDER, IntPtr bstrMessage);

    [DispId(104)]
    int UnusedSendForeignStockProxyCancel(IntPtr bstrLogInID, IntPtr pOSSTOCKPROXYORDER, IntPtr bstrMessage);

    [DispId(105)]
    int UnusedSendOptionProxyOrder(IntPtr bstrLogInID, IntPtr pFUTUREPROXYORDER, IntPtr bstrMessage);

    [DispId(106)]
    int UnusedSendOptionProxyAlter(IntPtr bstrLogInID, IntPtr pFUTUREPROXYORDER, IntPtr bstrMessage);

    [DispId(107)]
    int UnusedSendDuplexProxyOrder(IntPtr bstrLogInID, IntPtr pFUTUREPROXYORDER, IntPtr bstrMessage);

    [DispId(108)]
    int UnusedSendOverseaFutureProxyAlter(IntPtr bstrLogInID, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(109)]
    int UnusedSendOverseaFutureSpreadProxyOrder(IntPtr bstrLogInID, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(110)]
    int UnusedSendOverseaOptionProxyOrder(IntPtr bstrLogInID, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(111)]
    int UnusedSendStockStrategyMMIT(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(112)]
    int UnusedSendStockStrategyAB(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(113)]
    int UnusedSendStockStrategyCB(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(114)]
    int UnusedSendStockStrategyMBA(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(115)]
    int UnusedSendStockStrategyLLS(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(116)]
    int UnusedSendFutureABOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(117)]
    int UnusedSendOverSeaFutureOCOOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(118)]
    int UnusedCancelOFStrategyOrder(IntPtr bstrLogInID, IntPtr pCancelOrder, IntPtr bstrMessage);

    [DispId(119)]
    int UnusedGetOFSmartStrategyReport(
        IntPtr bstrLogInID,
        IntPtr bstrAccount,
        IntPtr bstrMarketType,
        IntPtr nReportStatus,
        IntPtr bstrKind,
        IntPtr bstrDate);

    [DispId(120)]
    int UnusedSendOverSeaFutureABOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(121)]
    int UnusedSpecialRequest(
        IntPtr bstrLogInID,
        IntPtr bstrTradeType,
        IntPtr bstrApplyDate,
        IntPtr bstrStockID,
        IntPtr bstrQty,
        IntPtr bstrAmt,
        IntPtr bstrBrokerID,
        IntPtr bstrAcno,
        IntPtr bstrPaymentDate);

    /// <summary>委託回報查詢(手冊 4-2-97;阻塞式、每次間隔 5 秒、只有國內證期選;nFormat 1 全部 … 9 預約),直接回字串。</summary>
    [DispId(122)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetOrderReport(
        [MarshalAs(UnmanagedType.BStr)] string bstrLogInID,
        [MarshalAs(UnmanagedType.BStr)] string bstrAccount,
        int nFormat);

    /// <summary>成交回報查詢(手冊 4-2-98;阻塞式、每次間隔 5 秒;nFormat 1 完整 … 5 T+1),直接回字串。</summary>
    [DispId(123)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetFulfillReport(
        [MarshalAs(UnmanagedType.BStr)] string bstrLogInID,
        [MarshalAs(UnmanagedType.BStr)] string bstrAccount,
        int nFormat);

    [DispId(124)]
    int UnusedSendStockStrategyFTLDayTrade(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(125)]
    int UnusedCancelStrategyList(IntPtr bstrLogInID, IntPtr pCancelOrder, IntPtr bstrMessage);

    [DispId(126)]
    int UnusedSendForeignStockOrderOLID(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr pAsyncOrder,
        IntPtr bstrOrderLinkedID,
        IntPtr bstrMessage);

    [DispId(127)]
    int UnusedSGXOverSeaFutureOrderOLID(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr pAsyncOrder,
        IntPtr bstrOrderLinkedID,
        IntPtr bstrMessage);

    [DispId(128)]
    int UnusedGetUpdateTradeData(
        IntPtr bstrLogInID,
        IntPtr bstrFunction,
        IntPtr bstrTradeType,
        IntPtr bstrStockID,
        IntPtr bstrSDate,
        IntPtr bstrEDate);

    [DispId(129)]
    int UnusedAllCoverDisOptions(IntPtr bstrLogInID, IntPtr pOrder, IntPtr bstrMessage);

    [DispId(130)]
    int UnusedSendTFOffsetNew(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr nCommodity,
        IntPtr bstrYearMonth,
        IntPtr nBuySell,
        IntPtr nQty,
        IntPtr nQty_2,
        IntPtr nQty_3,
        IntPtr bstrMessage);

    [DispId(131)]
    int UnusedGetAvgCost(IntPtr bstrLogInID, IntPtr pGWQuery, IntPtr bstrMessage);

    [DispId(132)]
    int UnusedOverSeaCancelOrderBySeqNoOLID(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSeqNo,
        IntPtr bstrOrderLinkedID,
        IntPtr bstrMessage);

    [DispId(133)]
    int UnusedOverSeaDecreaseOrderBySeqNoOLID(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrSeqNo,
        IntPtr nDecreaseQty,
        IntPtr bstrOrderLinkedID,
        IntPtr bstrMessage);

    [DispId(134)]
    int UnusedOverSeaCorrectPriceByBookNoOLID(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr pAsyncOrder,
        IntPtr bstrOrderLinkedID,
        IntPtr bstrMessage);

    [DispId(135)]
    int UnusedOverSeaCorrectPriceSpreadByBookNoOLID(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr pAsyncOrder,
        IntPtr bstrOrderLinkedID,
        IntPtr bstrMessage);

    [DispId(136)]
    int UnusedCancelOrderByStockNoAdvance(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr bstrAccount,
        IntPtr bstrStockNo,
        IntPtr nBuySell,
        IntPtr bstrPrice,
        IntPtr bstrMessage);

    [DispId(137)]
    int UnusedGetBalance(IntPtr bstrLogInID);

    [DispId(138)]
    int UnusedGetT3DueAmt(IntPtr bstrLogInID, IntPtr bstrAccount);

    [DispId(139)]
    int UnusedGetIPAAmt(IntPtr bstrLogInID, IntPtr bstrAccount, IntPtr nMarket);

    [DispId(140)]
    int UnusedGetBankBlock(IntPtr bstrLogInID, IntPtr bstrAccount);

    [DispId(141)]
    int UnusedGetNTDBlock(IntPtr bstrLogInID, IntPtr bstrAccount);

    [DispId(142)]
    int UnusedSendOverseaFutureSpreadOrder2(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(143)]
    int UnusedSendOverseaFutureSpreadOrder2OLID(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr pAsyncOrder,
        IntPtr bstrOrderLinkedID,
        IntPtr bstrMessage);

    [DispId(144)]
    int UnusedSendOverseaFutureSpreadProxyOrder2(IntPtr bstrLogInID, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(145)]
    int UnusedAlterTFTOOrder(IntPtr bstrLogInID, IntPtr bAsyncOrder, IntPtr pAsyncOrder, IntPtr bstrMessage);

    [DispId(146)]
    int UnusedSendStockProxyPreAlter(IntPtr bstrLogInID, IntPtr pSTOCKPROXYORDER, IntPtr bstrMessage);

    [DispId(147)]
    int UnusedCapitalPayWithDraw(
        IntPtr bstrLogInID,
        IntPtr bstrBankCode,
        IntPtr bstrSwiftCode,
        IntPtr bstrBankAcno,
        IntPtr bstrDollars,
        IntPtr bstrMessage);

    [DispId(148)]
    int UnusedSendOverseaOptionOrderOLID(
        IntPtr bstrLogInID,
        IntPtr bAsyncOrder,
        IntPtr pAsyncOrder,
        IntPtr bstrOrderLinkedID,
        IntPtr bstrMessage);

    /// <summary>海外期貨未平倉明細(手冊沒有章節,型別庫才有),由 OnOverseaFutureOpenInterestWithDetails 回傳。</summary>
    [DispId(149)]
    int GetOFOpenInterestWithDetails(
        [MarshalAs(UnmanagedType.BStr)] string bstrLogInID,
        [MarshalAs(UnmanagedType.BStr)] string bstrAccount);
}

/// <summary>_ISKOrderLibEvents:帳務查詢的回傳事件(只宣告查詢用得到的;委託類事件不接)。</summary>
[ComImport]
[Guid("4E81123D-980C-419E-889C-184ED17DC4D6")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISKOrderLibEvents
{
    /// <summary>帳號(手冊 4-2-a):市場,分公司代碼,分公司,帳號,身份證字號,姓名;期貨的分公司代碼是 Broker id。</summary>
    [DispId(1)]
    void OnAccount([MarshalAs(UnmanagedType.BStr)] string bstrLogInID, [MarshalAs(UnmanagedType.BStr)] string bstrAccountData);

    /// <summary>國內期貨未平倉(手冊 4-2-d),以 ## 結尾;查無資料回 001,查無資料,帳號。</summary>
    [DispId(4)]
    void OnOpenInterest([MarshalAs(UnmanagedType.BStr)] string bstrData);

    /// <summary>海外期貨未平倉(舊版查詢,手冊 4-2-e)。</summary>
    [DispId(5)]
    void OnOverseaFutureOpenInterest([MarshalAs(UnmanagedType.BStr)] string bstrData);

    /// <summary>國內期貨權益數(手冊 4-2-i),以 ## 結尾。</summary>
    [DispId(9)]
    void OnFutureRights([MarshalAs(UnmanagedType.BStr)] string bstrData);

    /// <summary>海外期貨權益數(手冊 4-2-k)。</summary>
    [DispId(12)]
    void OnOverSeaFutureRight([MarshalAs(UnmanagedType.BStr)] string bstrData);

    /// <summary>海外期貨未平倉彙總 / 明細(手冊 4-2-q):第一筆 @@,代碼,訊息,以 ## 結尾。</summary>
    [DispId(18)]
    void OnOFOpenInterestGWReport([MarshalAs(UnmanagedType.BStr)] string bstrData);

    /// <summary>國內未平倉查詢狀態(手冊 4-2-t;0 成功、1 失敗)。</summary>
    [DispId(25)]
    void OnOpenInterestGWStatus(int nQueryStatus, [MarshalAs(UnmanagedType.BStr)] string bstrErrorMsg);

    /// <summary>海外未平倉查詢狀態(手冊 4-2-u)。</summary>
    [DispId(26)]
    void OnOverseaFutureOpenInterestGWStatus(int nQueryStatus, [MarshalAs(UnmanagedType.BStr)] string bstrErrorMsg);

    /// <summary>國內權益數查詢狀態(手冊 4-2-v)。</summary>
    [DispId(27)]
    void OnFutureRightsStatus(int nQueryStatus, [MarshalAs(UnmanagedType.BStr)] string bstrErrorMsg);

    /// <summary>海外權益數查詢狀態(手冊 4-2-w)。</summary>
    [DispId(28)]
    void OnOverseaFutureRightsStatus(int nQueryStatus, [MarshalAs(UnmanagedType.BStr)] string bstrErrorMsg);

    /// <summary>國內未平倉一次全部(JSON 陣列,手冊 4-2-x)。</summary>
    [DispId(29)]
    void OnOpenInterestJson([MarshalAs(UnmanagedType.BStr)] string bstrData);

    /// <summary>海外未平倉明細(GetOFOpenInterestWithDetails;手冊沒有章節)。</summary>
    [DispId(30)]
    void OnOverseaFutureOpenInterestWithDetails([MarshalAs(UnmanagedType.BStr)] string bstrData);

    /// <summary>國內未平倉明細(手冊沒有章節,觸發來源待實測)。</summary>
    [DispId(31)]
    void OnOpenInterestWithDetails([MarshalAs(UnmanagedType.BStr)] string bstrData);
}
