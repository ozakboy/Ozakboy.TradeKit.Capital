namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 國內報價不可用或斷線的原因(<see cref="CapitalHostSession.DomesticReason"/>、
/// <see cref="CapitalDomesticStateChangedEventArgs.Reason"/>、錯誤的 <see cref="CapitalErrorDataKeys.Reason"/>)。字串是公開契約。
/// Why the domestic quote side is unavailable or disconnected. The strings are part of the public contract.
/// </summary>
public static class CapitalDomesticReasons
{
    /// <summary>
    /// 國內 EnterMonitorLONG 回了錯誤代碼(在 <see cref="CapitalDomesticStateChangedEventArgs.Code"/>);常見原因是帳號沒有國內報價權限。
    /// The domestic EnterMonitorLONG answered an error code; commonly the account lacks the domestic quote permission.
    /// </summary>
    public const string EnterRejected = "enter_rejected";

    /// <summary>60 秒內沒有收到 3003(商品基本資料完成)。No 3003 within 60 seconds.</summary>
    public const string ReadyTimeout = "ready_timeout";

    /// <summary>國內報價主機斷線(3002),正在退避重連。The domestic server disconnected (3002); reconnecting.</summary>
    public const string ConnectionLost = "connection_lost";

    /// <summary>SKQuoteLib 沒有註冊。SKQuoteLib is not registered.</summary>
    public const string SkComNotRegistered = "skcom_not_registered";

    /// <summary>SKQuoteLib 已註冊但 DLL 不在。SKQuoteLib is registered but its DLL is missing.</summary>
    public const string SkComFileMissing = "skcom_file_missing";

    /// <summary>建立 SKQuoteLib 或掛它的事件失敗。Creating SKQuoteLib or attaching its events failed.</summary>
    public const string SkComCreateFailed = "skcom_create_failed";
}
