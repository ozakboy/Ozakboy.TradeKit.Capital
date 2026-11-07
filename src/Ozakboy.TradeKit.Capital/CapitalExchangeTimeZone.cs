namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一個群益交易所代碼對應的時區。
/// The time zone of one Capital exchange code.
/// </summary>
/// <param name="Exchange">群益交易所代碼(<c>FX</c>、<c>CME</c>…)。Capital's exchange code.</param>
/// <param name="WindowsTimeZoneId">Windows 時區 ID(例如 <c>Eastern Standard Time</c>)。The Windows time zone id.</param>
/// <param name="IanaTimeZoneId">IANA 時區 ID(非 Windows 平台用,例如 <c>America/New_York</c>)。The IANA id.</param>
/// <param name="IsVerified">
/// 是否以實際成交時間與接收時間對照確認過;<see langword="false"/> 只是依交易所所在地推定,呼叫端可據此限制使用。
/// Whether it was confirmed against real tick and receive times; false means inferred from the exchange's location only.
/// </param>
public sealed record CapitalExchangeTimeZone(string Exchange, string WindowsTimeZoneId, string IanaTimeZoneId, bool IsVerified)
{
    private TimeZoneInfo? _timeZone;

    /// <summary>
    /// 對應的 <see cref="TimeZoneInfo"/>(先找 Windows ID,找不到再找 IANA ID;第一次取用時解析)。
    /// The matching <see cref="TimeZoneInfo"/>, looked up by the Windows id and then the IANA id on first use.
    /// </summary>
    /// <exception cref="TimeZoneNotFoundException">本機兩種 ID 都找不到。Neither id exists on this machine.</exception>
    public TimeZoneInfo TimeZone => _timeZone ??= Resolve();

    private TimeZoneInfo Resolve()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(WindowsTimeZoneId, out TimeZoneInfo? zone)
            || TimeZoneInfo.TryFindSystemTimeZoneById(IanaTimeZoneId, out zone))
        {
            return zone;
        }

        throw new TimeZoneNotFoundException($"本機找不到時區 {WindowsTimeZoneId} / {IanaTimeZoneId}。Time zone {WindowsTimeZoneId} / {IanaTimeZoneId} was not found.");
    }
}
