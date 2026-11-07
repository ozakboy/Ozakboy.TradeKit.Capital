namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// <see cref="Error.Data"/> 用到的鍵。字串是公開契約,發佈後不再變更。
/// The keys used in <see cref="Error.Data"/>. The strings are part of the public contract.
/// </summary>
public static class CapitalErrorDataKeys
{
    /// <summary>群益的原始代碼(整數)。Capital's raw code, an integer.</summary>
    public const string Code = "code";

    /// <summary>群益代碼的說明文字。Capital's text for the code.</summary>
    public const string Message = "message";

    /// <summary>登入失敗是否因帳號被鎖(群益 307)。Whether a failed login means the account is locked (Capital 307).</summary>
    public const string Locked = "locked";

    /// <summary>COM 的 HRESULT(<c>0x80040154</c> 形式的字串)。The COM HRESULT as a 0x… string.</summary>
    public const string HResult = "hresult";

    /// <summary>宿主回報的原因(<c>skcom_not_registered</c> 等)。The reason the host reported.</summary>
    public const string Reason = "reason";

    /// <summary>相關的商品(「交易所,代碼」)。The symbol involved, "exchange,code".</summary>
    public const string Symbol = "symbol";

    /// <summary>宿主行程的結束碼。The host process's exit code.</summary>
    public const string ExitCode = "exitCode";

    /// <summary>請求的 method。The request's method.</summary>
    public const string Method = "method";

    /// <summary>
    /// 國內報價當時的狀態(協定字串 <c>connecting</c> / <c>ready</c> / <c>disconnected</c> / <c>unavailable</c>)。
    /// The domestic quote state at the time, as its wire string.
    /// </summary>
    public const string DomesticState = "domestic";
}
