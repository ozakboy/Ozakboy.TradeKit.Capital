namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 帳務型別 <c>ToString</c> 用的帳號遮罩:只留末 3 碼(短於 4 碼全遮)。
/// The account masking used by the account types' ToString: only the last 3 characters stay (shorter values are fully masked).
/// </summary>
internal static class CapitalAccountText
{
    /// <summary>
    /// 遮罩帳號。
    /// Masks an account.
    /// </summary>
    /// <param name="account">帳號。The account.</param>
    /// <returns><c>***567</c> 這樣的文字;null 或空字串回空字串。Masked text, or empty.</returns>
    public static string Mask(string? account)
    {
        if (string.IsNullOrEmpty(account))
        {
            return string.Empty;
        }

        return account.Length < 4 ? "***" : "***" + account[^3..];
    }
}
