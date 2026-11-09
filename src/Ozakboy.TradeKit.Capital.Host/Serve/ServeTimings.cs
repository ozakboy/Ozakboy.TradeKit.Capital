namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// <c>serve</c> 模式的各種時限(預設值照 DESIGN §2;測試與假宿主可縮短)。
/// The serve mode's timings; defaults follow DESIGN §2, and tests and the fake host may shorten them.
/// </summary>
internal sealed class ServeTimings
{
    /// <summary>EnterMonitorLONG 後等 3001 的上限(60 秒)。How long to wait for 3001 after EnterMonitorLONG.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>商品清單與商品明細各自的上限(60 秒)。The limit for the product list and for the details, each.</summary>
    public TimeSpan ProductsTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>K 線:最後一筆之後多久沒新資料算結束(1.5 秒)。K-lines end this long after the last row.</summary>
    public TimeSpan KLineQuiet { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>K 線:一筆都沒來時的上限(10 秒)。K-lines end after this long with no row at all.</summary>
    public TimeSpan KLineNoData { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>K 線靜默偵測的輪詢間隔。The polling interval of the K-line quiet detection.</summary>
    public TimeSpan KLinePoll { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 國內 EnterMonitorLONG 後等 3003 的上限(60 秒;實測約 0.5 秒)。首次逾時 → 國內不可用;重連時逾時 → 稍後再試。
    /// How long to wait for the domestic 3003 after EnterMonitorLONG (measured about 0.5 s). A first-time timeout makes
    /// the domestic side unavailable; a timeout while reconnecting retries later.
    /// </summary>
    public TimeSpan DomesticReadyTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>國內 K 線等 OnKLineComplete 的上限(60 秒)。How long a domestic K-line request waits for OnKLineComplete.</summary>
    public TimeSpan DomesticKLineTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 0.3.0:海外選擇權 EnterMonitorLONG 後等連線事件的上限(60 秒,比照國內;待實測)。首次逾時 → 不可用;重連時逾時 → 稍後再試。
    /// How long to wait for the overseas option connect event; a first-time timeout makes it unavailable.
    /// </summary>
    public TimeSpan OverseasOptionsReadyTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 0.3.0:以事件回傳的帳務查詢,最後一則事件之後安靜多久算結束(6 秒,沿用 probe-full)。
    /// An event-based account query ends after this much quiet; 6 seconds, as in probe-full.
    /// </summary>
    public TimeSpan AccountQuiet { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// 0.3.0:收過查詢狀態事件之後安靜多久算結束(2 秒,沿用 probe-full)。
    /// After the query status event, the query ends after this much quiet; 2 seconds, as in probe-full.
    /// </summary>
    public TimeSpan AccountQuietAfterStatus { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>0.3.0:一個帳務查詢的上限(60 秒)。The limit of one account query.</summary>
    public TimeSpan AccountQueryLimit { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>0.3.0:帳務查詢靜默偵測的輪詢間隔。The polling interval of the account quiet detection.</summary>
    public TimeSpan AccountPoll { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 0.3.0:委託 / 成交回報查詢兩次之間的最小間隔(手冊 4-2-97、4-2-98「每次查詢間需間隔五秒」,多留半秒)。兩個查詢共用同一個間隔。
    /// The minimum gap between two order or fill report queries (the manual asks for five seconds; half a second is
    /// added). Both queries share the gap.
    /// </summary>
    public TimeSpan ReportInterval { get; init; } = TimeSpan.FromSeconds(5.5);

    /// <summary>
    /// 3002 之後重新 EnterMonitorLONG 的退避(5、10、30、60 秒,之後一律 60 秒);海外與國內各自計次、共用這份清單。
    /// The reconnect backoff; the overseas and the domestic side count their attempts separately and share this list.
    /// </summary>
    public IReadOnlyList<TimeSpan> ReconnectDelays { get; init; } =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    /// <summary>
    /// 第 <paramref name="attempt"/> 次(從 0 起)重連前的等待;超過清單長度用最後一個。
    /// The wait before reconnect attempt <paramref name="attempt"/> (from 0); the last entry repeats.
    /// </summary>
    /// <param name="attempt">第幾次。Attempt number.</param>
    /// <returns>等待時間。The wait.</returns>
    public TimeSpan ReconnectDelay(int attempt)
    {
        if (ReconnectDelays.Count == 0)
        {
            return TimeSpan.FromSeconds(60);
        }

        return ReconnectDelays[Math.Clamp(attempt, 0, ReconnectDelays.Count - 1)];
    }
}
