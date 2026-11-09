namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 海外報價主機的連線狀態(<c>IsConnected</c> + <c>GetQuoteStatus</c>;實測連線數回 0、未超限)。
/// The overseas quote connection status (measured: 0 connections, not over the limit).
/// </summary>
public sealed record CapitalConnectionStatus
{
    /// <summary>是否連線。Whether connected.</summary>
    public required bool Connected { get; init; }

    /// <summary>連線數(<c>GetQuoteStatus</c>)。The connection count.</summary>
    public required int Connections { get; init; }

    /// <summary>是否超過連線上限。Whether over the connection limit.</summary>
    public required bool OutLimit { get; init; }

    /// <summary>
    /// 國內報價的狀態(0.2.0 起;0.1.x 的宿主沒有這個欄位時為 <see cref="CapitalDomesticState.NotStarted"/>)。
    /// The domestic quote state (since 0.2.0; NotStarted when an older host does not report it).
    /// </summary>
    public CapitalDomesticState Domestic { get; init; }

    /// <summary>國內不可用或斷線的原因(<see cref="CapitalDomesticReasons"/>)。Why the domestic side is unavailable or disconnected.</summary>
    public string? DomesticReason { get; init; }

    /// <summary>0.3.0:海外選擇權報價的狀態(舊宿主沒有這個欄位時為 NotStarted)。The overseas option state.</summary>
    public CapitalOverseasOptionsState OverseasOptions { get; init; }

    /// <summary>0.3.0:海外選擇權不可用或斷線的原因。Why the overseas options are unavailable or disconnected.</summary>
    public string? OverseasOptionsReason { get; init; }

    /// <summary>0.3.0:SKCOM 版本(宿主讀不到時為 null)。The SKCOM version; null when the host could not read it.</summary>
    public string? SkcomVersion { get; init; }
}
