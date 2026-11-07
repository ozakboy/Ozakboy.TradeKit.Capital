namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// <see cref="CapitalHostSession"/> 的設定。
/// The settings of <see cref="CapitalHostSession"/>.
/// </summary>
public sealed class CapitalHostOptions
{
    /// <summary>宿主執行檔的檔名。The host executable's file name.</summary>
    public const string HostExecutableName = "Ozakboy.TradeKit.Capital.Host.exe";

    /// <summary>套件把宿主複製到輸出目錄的哪個子資料夾。The output subfolder the package copies the host to.</summary>
    public const string HostFolderName = "capital-host";

    /// <summary>
    /// 宿主執行檔的完整路徑(必填)。通常是 <see cref="DefaultHostPath"/>(<c>AppContext.BaseDirectory</c>)。
    /// The full path of the host executable (required); usually <see cref="DefaultHostPath"/> of
    /// <c>AppContext.BaseDirectory</c>.
    /// </summary>
    public string HostExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// SKCOM 自己的紀錄目錄(<c>CAPITAL_LOG_DIR</c> → <c>SKCenterLib_SetLogPath</c>);<see langword="null"/> 用 SKCOM 預設。
    /// SKCOM 的紀錄可能含登入帳號,請放在使用者自己的資料夾,不要放進版控或分享。
    /// SKCOM's own log directory; <see langword="null"/> uses SKCOM's default. SKCOM's logs may contain the login ID, so
    /// keep them in the user's own folder, never in version control or anything shared.
    /// </summary>
    public string? LogDirectory { get; set; }

    /// <summary>
    /// 啟動到 ready 的上限,預設 90 秒(登入 + 連線最多 60 秒 + 餘裕)。
    /// How long a start may take until ready; 90 seconds by default (login, up to 60 s of connecting, and slack).
    /// </summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// 宿主意外結束後最多連續重啟幾次,預設 5;超過就進入 <see cref="CapitalHostState.Faulted"/>。
    /// How many consecutive restarts an unexpected host exit gets; 5 by default, then <see cref="CapitalHostState.Faulted"/>.
    /// </summary>
    public int MaxRestarts { get; set; } = 5;

    /// <summary>
    /// 事件佇列容量,預設 262,144;滿了丟最舊的並計入 <see cref="CapitalHostSession.DroppedEvents"/>。
    /// 訂閱當下群益會回補當個交易日的全部成交(實測 7 檔約 20.8 萬筆),容量太小會把回補丟掉。
    /// The event queue capacity; 262,144 by default. When full the oldest is dropped and counted in
    /// <see cref="CapitalHostSession.DroppedEvents"/>. Capital replays the whole trading day's ticks at subscription
    /// (about 208,000 for 7 symbols, measured), so a small capacity drops the replay.
    /// </summary>
    public int EventCapacity { get; set; } = 262_144;

    /// <summary>重啟的退避(測試可改短)。The restart backoff; tests may shorten it.</summary>
    internal IReadOnlyList<TimeSpan> RestartDelays { get; set; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>重啟後穩定跑過這段時間,連續重啟計數歸零(測試可改)。After this long ready, the restart count starts over.</summary>
    internal TimeSpan StableAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>額外的子行程環境變數(只給測試驅動假宿主;帳密鍵一律被覆寫)。Extra child environment, for tests only.</summary>
    internal Dictionary<string, string> AdditionalEnvironment { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 套件預設的宿主位置:<c>{baseDirectory}\capital-host\Ozakboy.TradeKit.Capital.Host.exe</c>。
    /// The package's default host location.
    /// </summary>
    /// <param name="baseDirectory">應用程式目錄,通常是 <c>AppContext.BaseDirectory</c>。The app directory.</param>
    /// <returns>宿主執行檔路徑。The host executable path.</returns>
    /// <exception cref="ArgumentException"><paramref name="baseDirectory"/> 空白。Blank <paramref name="baseDirectory"/>.</exception>
    public static string DefaultHostPath(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        return Path.Combine(baseDirectory, HostFolderName, HostExecutableName);
    }

    /// <summary>
    /// 檢查設定是否可用(不檢查檔案存不存在,那在啟動時回 <see cref="CapitalErrorCodes.HostNotFound"/>)。
    /// Checks that the settings are usable; file existence is checked at start.
    /// </summary>
    /// <returns>可用時為成功。Success when usable.</returns>
    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(HostExecutablePath))
        {
            return CapitalErrors.InvalidOptions("HostExecutablePath 必填。HostExecutablePath is required.");
        }

        if (StartTimeout <= TimeSpan.Zero)
        {
            return CapitalErrors.InvalidOptions("StartTimeout 必須大於 0。StartTimeout must be positive.");
        }

        if (MaxRestarts < 0)
        {
            return CapitalErrors.InvalidOptions("MaxRestarts 不可為負數。MaxRestarts must not be negative.");
        }

        if (EventCapacity < 1)
        {
            return CapitalErrors.InvalidOptions("EventCapacity 必須至少 1。EventCapacity must be at least 1.");
        }

        return Result.Success();
    }
}
