namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// 從環境變數讀到的帳密與 SKCOM 紀錄目錄。讀完立刻從本行程環境清掉;不寫檔、不進日誌。
/// <see cref="ToString"/> 不含任何內容。
/// The login ID, password and SKCOM log directory read from the environment. They are removed from this process's
/// environment right after reading and never written to a file or a log. <see cref="ToString"/> carries no content.
/// </summary>
internal sealed class ServeLogin
{
    private ServeLogin(string loginId, string password, string? logDirectory)
    {
        LoginId = loginId;
        Password = password;
        LogDirectory = logDirectory;
    }

    /// <summary>身分證字號(已轉大寫;缺少時為空字串)。The login ID, upper case; empty when missing.</summary>
    public string LoginId { get; }

    /// <summary>密碼(缺少時為空字串)。The password; empty when missing.</summary>
    public string Password { get; }

    /// <summary>SKCOM 紀錄目錄(可空)。SKCOM's log directory, optional.</summary>
    public string? LogDirectory { get; }

    /// <summary>帳號與密碼都有值。Both the login ID and the password are present.</summary>
    public bool IsComplete => LoginId.Length > 0 && Password.Length > 0;

    /// <summary>
    /// 讀環境變數並立刻清掉(帳號、密碼、紀錄目錄三個都清)。
    /// Reads the environment variables and removes all three right away.
    /// </summary>
    /// <returns>讀到的值。The values read.</returns>
    public static ServeLogin ReadAndClear()
    {
        string loginId = (Environment.GetEnvironmentVariable(ServeEnvironmentNames.LoginId) ?? string.Empty).Trim().ToUpperInvariant();
        string password = Environment.GetEnvironmentVariable(ServeEnvironmentNames.Password) ?? string.Empty;
        string? logDirectory = Environment.GetEnvironmentVariable(ServeEnvironmentNames.LogDirectory);

        Environment.SetEnvironmentVariable(ServeEnvironmentNames.LoginId, null);
        Environment.SetEnvironmentVariable(ServeEnvironmentNames.Password, null);
        Environment.SetEnvironmentVariable(ServeEnvironmentNames.LogDirectory, null);

        return new ServeLogin(loginId, password, string.IsNullOrWhiteSpace(logDirectory) ? null : logDirectory.Trim());
    }

    /// <summary>
    /// 直接以值建立(測試用)。
    /// Creates from values directly (tests).
    /// </summary>
    /// <param name="loginId">身分證字號。The login ID.</param>
    /// <param name="password">密碼。The password.</param>
    /// <param name="logDirectory">紀錄目錄。Log directory.</param>
    /// <returns>登入資料。The login data.</returns>
    public static ServeLogin Create(string loginId, string password, string? logDirectory = null) =>
        new((loginId ?? string.Empty).Trim().ToUpperInvariant(), password ?? string.Empty, logDirectory);

    /// <inheritdoc/>
    public override string ToString() => nameof(ServeLogin) + " { *** }";
}
