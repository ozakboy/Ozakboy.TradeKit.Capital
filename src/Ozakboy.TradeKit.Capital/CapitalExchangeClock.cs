using System.Globalization;

namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 群益交易所當地時間 → UTC。群益的成交時間與 K 線時間都是交易所當地時間(不是台北時間),要依交易所的時區轉換,
/// 夏令時間由 <see cref="TimeZoneInfo"/> 自動處理。
/// Capital exchange local time to UTC. Capital's tick and K-line times are the exchange's local time (not Taipei), so
/// they are converted by the exchange's time zone, with daylight saving handled by <see cref="TimeZoneInfo"/>.
/// </summary>
/// <remarks>
/// <para>
/// 已實測(2026-11-06,成交時間對照接收時間,延遲 &lt; 1 秒):<c>FX</c>、<c>NYM</c> = 美東(<c>Eastern Standard Time</c>);
/// <c>CME</c>、<c>CBOT</c> = 芝加哥(<c>Central Standard Time</c>)。其餘交易所只依所在地推定,
/// <see cref="CapitalExchangeTimeZone.IsVerified"/> 為 <see langword="false"/>;<c>INDEX</c>(各國指數混在一起)與
/// <c>ICS</c>(價差)沒有對照,轉換會回 <see cref="CapitalErrorCodes.UnknownExchange"/>。
/// Measured (tick times against receive times, under 1 s of delay): FX and NYM are US Eastern; CME and CBOT are
/// Chicago. Every other exchange is inferred from its location with IsVerified false; INDEX (indices of many
/// countries) and ICS (spreads) have no mapping and fail with <see cref="CapitalErrorCodes.UnknownExchange"/>.
/// </para>
/// <para>
/// 分 K 的時間是那一分鐘的<b>結束</b>:開始時刻 = 結束 − 1 分鐘(<see cref="MinuteBarOpenUtc"/>)。
/// A minute bar's time is the <b>end</b> of its minute: open = end − 1 minute (<see cref="MinuteBarOpenUtc"/>).
/// </para>
/// </remarks>
public static class CapitalExchangeClock
{
    private const string Eastern = "Eastern Standard Time";
    private const string Central = "Central Standard Time";

    private static readonly Dictionary<string, CapitalExchangeTimeZone> Zones = Build(
        new("FX", Eastern, "America/New_York", true),
        new("NYM", Eastern, "America/New_York", true),
        new("CME", Central, "America/Chicago", true),
        new("CBOT", Central, "America/Chicago", true),
        new("CFE", Central, "America/Chicago", false),
        new("Cboe", Central, "America/Chicago", false),
        new("ICEUS", Eastern, "America/New_York", false),
        new("ICEEU", "GMT Standard Time", "Europe/London", false),
        new("Eurex", "W. Europe Standard Time", "Europe/Berlin", false),
        new("HKEx", "China Standard Time", "Asia/Hong_Kong", false),
        new("SGX", "Singapore Standard Time", "Asia/Singapore", false),
        new("ICESG", "Singapore Standard Time", "Asia/Singapore", false),
        new("OSE", "Tokyo Standard Time", "Asia/Tokyo", false),
        new("TCE", "Tokyo Standard Time", "Asia/Tokyo", false),
        new("TFX", "Tokyo Standard Time", "Asia/Tokyo", false),
        new("KRX", "Korea Standard Time", "Asia/Seoul", false),
        new("ASX", "AUS Eastern Standard Time", "Australia/Sydney", false));

    /// <summary>
    /// 所有已知的交易所時區。
    /// Every known exchange time zone.
    /// </summary>
    public static IReadOnlyCollection<CapitalExchangeTimeZone> Known => Zones.Values;

    /// <summary>
    /// 查交易所的時區(區分大小寫,照群益原樣)。
    /// Looks up an exchange's time zone, case-sensitively as Capital sends it.
    /// </summary>
    /// <param name="exchange">交易所代碼。The exchange code.</param>
    /// <param name="zone">時區。The time zone.</param>
    /// <returns>有對照時為 <see langword="true"/>。True when mapped.</returns>
    public static bool TryGetTimeZone(string exchange, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CapitalExchangeTimeZone? zone)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        return Zones.TryGetValue(exchange, out zone);
    }

    /// <summary>
    /// 交易所當地時間 → UTC。不存在的當地時間(夏令開始那一小時)以標準時間的位移換算;重複的當地時間(夏令結束)取標準時間。
    /// Exchange local time to UTC. A local time that does not exist (the hour skipped when daylight saving starts) uses
    /// the standard offset; an ambiguous one (when it ends) is read as standard time.
    /// </summary>
    /// <param name="exchange">交易所代碼。The exchange code.</param>
    /// <param name="local">當地時間(Kind 不論)。The local time; its Kind is ignored.</param>
    /// <returns>UTC 時間,或 <see cref="CapitalErrorCodes.UnknownExchange"/>。The UTC time or an unknown-exchange failure.</returns>
    public static Result<DateTimeOffset> ToUtc(string exchange, DateTime local)
    {
        ArgumentNullException.ThrowIfNull(exchange);

        if (!Zones.TryGetValue(exchange, out CapitalExchangeTimeZone? zone))
        {
            return CapitalErrors.UnknownExchange(exchange);
        }

        TimeZoneInfo timeZone = zone.TimeZone;
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        TimeSpan offset = timeZone.IsInvalidTime(unspecified) ? timeZone.BaseUtcOffset : timeZone.GetUtcOffset(unspecified);

        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    /// <summary>
    /// 群益成交的 <c>nDate</c>(yyyyMMdd)+ <c>nTime</c>(HHmmss)→ UTC。
    /// Capital's tick date (yyyyMMdd) and time (HHmmss) to UTC.
    /// </summary>
    /// <param name="exchange">交易所代碼。The exchange code.</param>
    /// <param name="date">yyyyMMdd,例如 <c>20261105</c>。For example 20261105.</param>
    /// <param name="time">HHmmss,例如 <c>224357</c>(前導 0 可省)。For example 224357; leading zeros optional.</param>
    /// <returns>UTC 時間,或失敗(<see cref="CapitalErrorCodes.InvalidArgument"/> / <see cref="CapitalErrorCodes.UnknownExchange"/>)。</returns>
    public static Result<DateTimeOffset> TickToUtc(string exchange, string date, string time)
    {
        ArgumentNullException.ThrowIfNull(date);
        ArgumentNullException.ThrowIfNull(time);

        string paddedTime = time.Trim().PadLeft(6, '0');

        if (!DateTime.TryParseExact(
                date.Trim() + paddedTime,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime local))
        {
            return CapitalErrors.InvalidArgument($"成交時間讀不懂:{date} {time}。The tick time could not be read: {date} {time}.");
        }

        return ToUtc(exchange, local);
    }

    /// <summary>
    /// 分 K 的開始時刻(UTC):群益給的是那一分鐘的結束(當地),先轉 UTC 再減 1 分鐘。
    /// A minute bar's open time in UTC: Capital gives the end of the minute in local time, converted and minus one minute.
    /// </summary>
    /// <param name="exchange">交易所代碼。The exchange code.</param>
    /// <param name="localEnd">當地的結束時刻。The local end time.</param>
    /// <returns>開始時刻(UTC)。The open time, UTC.</returns>
    public static Result<DateTimeOffset> MinuteBarOpenUtc(string exchange, DateTime localEnd) =>
        ToUtc(exchange, localEnd).Map(end => end.AddMinutes(-1));

    private static Dictionary<string, CapitalExchangeTimeZone> Build(params CapitalExchangeTimeZone[] zones)
    {
        Dictionary<string, CapitalExchangeTimeZone> map = new(StringComparer.Ordinal);

        foreach (CapitalExchangeTimeZone zone in zones)
        {
            map.Add(zone.Exchange, zone);
        }

        return map;
    }
}
