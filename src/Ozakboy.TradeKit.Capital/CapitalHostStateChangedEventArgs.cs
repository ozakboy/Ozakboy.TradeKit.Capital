namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// <see cref="CapitalHostSession.StateChanged"/> 的內容。
/// The payload of <see cref="CapitalHostSession.StateChanged"/>.
/// </summary>
public sealed class CapitalHostStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 建立內容。
    /// Creates the payload.
    /// </summary>
    /// <param name="previous">之前的狀態。The previous state.</param>
    /// <param name="current">現在的狀態。The current state.</param>
    public CapitalHostStateChangedEventArgs(CapitalHostState previous, CapitalHostState current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>之前的狀態。The previous state.</summary>
    public CapitalHostState Previous { get; }

    /// <summary>現在的狀態。The current state.</summary>
    public CapitalHostState Current { get; }
}
