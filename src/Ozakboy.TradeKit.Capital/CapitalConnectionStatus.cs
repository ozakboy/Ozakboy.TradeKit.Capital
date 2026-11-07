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
}
