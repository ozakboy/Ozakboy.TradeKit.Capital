namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// <see cref="CapitalHostSession.OverseasOptionsStateChanged"/> 的內容。
/// The payload of <see cref="CapitalHostSession.OverseasOptionsStateChanged"/>.
/// </summary>
public sealed class CapitalOverseasOptionsStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 建立內容。
    /// Creates the payload.
    /// </summary>
    /// <param name="previous">之前的狀態。The previous state.</param>
    /// <param name="current">現在的狀態。The current state.</param>
    /// <param name="reason">原因(<see cref="CapitalDomesticReasons"/> 的字串;沒有時為 null)。The reason, if any.</param>
    /// <param name="code">群益代碼(EnterMonitorLONG 被拒時)。Capital's code, when EnterMonitorLONG was rejected.</param>
    public CapitalOverseasOptionsStateChangedEventArgs(CapitalOverseasOptionsState previous, CapitalOverseasOptionsState current, string? reason, int? code)
    {
        Previous = previous;
        Current = current;
        Reason = reason;
        Code = code;
    }

    /// <summary>之前的狀態。The previous state.</summary>
    public CapitalOverseasOptionsState Previous { get; }

    /// <summary>現在的狀態。The current state.</summary>
    public CapitalOverseasOptionsState Current { get; }

    /// <summary>原因;就緒或連線中時為 null。The reason; null while ready or connecting.</summary>
    public string? Reason { get; }

    /// <summary>群益代碼(EnterMonitorLONG 被拒時)。Capital's code, when EnterMonitorLONG was rejected.</summary>
    public int? Code { get; }
}
