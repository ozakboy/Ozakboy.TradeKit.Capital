namespace Ozakboy.TradeKit.Capital;

// 檔名刻意避開 credentials 字樣:機敏檔掃描工具以檔名比對,原始碼檔不該被當成憑證檔(同 Shioaji 套件的 ShioajiBrokerLogin.cs)。
// The file name avoids the word "credentials": secret-file scanners match on file names, and a source file should not
// look like a credentials file (as with ShioajiBrokerLogin.cs in the Shioaji package).

/// <summary>
/// 群益登入用的身分證字號與密碼。只放在記憶體;<see cref="ToString"/> 不含任何內容,也不是 record(避免自動產生的
/// ToString 與相等比較把值帶出去)。
/// The ID number and password for Capital's login. Kept in memory only; <see cref="ToString"/> carries no content, and
/// this is not a record so no generated ToString or equality ever leaks the values.
/// </summary>
public sealed class CapitalCredentials
{
    /// <summary>
    /// 建立帳密。
    /// Creates the credentials.
    /// </summary>
    /// <param name="loginId">身分證字號(大小寫皆可,送出前轉大寫)。The ID number; upper-cased before use.</param>
    /// <param name="password">登入密碼。The login password.</param>
    /// <exception cref="ArgumentNullException">任一參數為 null。Either argument is null.</exception>
    public CapitalCredentials(string loginId, string password)
    {
        ArgumentNullException.ThrowIfNull(loginId);
        ArgumentNullException.ThrowIfNull(password);

        LoginId = loginId.Trim().ToUpperInvariant();
        Password = password;
    }

    /// <summary>身分證字號(已轉大寫、去前後空白)。The ID number, upper case and trimmed.</summary>
    public string LoginId { get; }

    /// <summary>登入密碼。The login password.</summary>
    public string Password { get; }

    /// <summary>
    /// 檢查格式:身分證字號為 1 個英文字母 + 9 個數字(轉大寫後比對),密碼不可空白。失敗回
    /// <see cref="CapitalErrorCodes.InvalidCredentials"/>,訊息不回顯任何字元。
    /// Checks the format: the ID is one letter followed by nine digits (compared after upper-casing) and the password
    /// is not blank. A failure is <see cref="CapitalErrorCodes.InvalidCredentials"/> and its message echoes no character.
    /// </summary>
    /// <returns>格式正確時為成功。Success when well-formed.</returns>
    public Result Validate()
    {
        if (!IsValidLoginId(LoginId))
        {
            return CapitalErrors.InvalidCredentials(
                "身分證字號格式不對:須為 1 個英文字母加 9 個數字。The ID number must be one letter followed by nine digits.");
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            return CapitalErrors.InvalidCredentials("登入密碼不可空白。The login password must not be blank.");
        }

        return Result.Success();
    }

    /// <summary>
    /// 不含任何內容。
    /// Carries no content.
    /// </summary>
    /// <returns>固定字串。A fixed string.</returns>
    public override string ToString() => nameof(CapitalCredentials) + " { *** }";

    private static bool IsValidLoginId(string value)
    {
        if (value.Length != 10 || value[0] is < 'A' or > 'Z')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            if (value[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
