using System.Runtime.InteropServices.ComTypes;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

/// <summary>
/// 把事件接收器掛到 COM 物件的連接點(IConnectionPoint.Advise),Dispose 時解除。
/// Attaches an event sink to a COM object's connection point and detaches it on Dispose.
/// </summary>
internal sealed class ComEventConnection : IDisposable
{
    private readonly IConnectionPoint _point;
    private readonly int _cookie;
    private bool _disposed;

    private ComEventConnection(IConnectionPoint point, int cookie)
    {
        _point = point;
        _cookie = cookie;
    }

    /// <summary>
    /// 掛上接收器。
    /// Attaches the sink.
    /// </summary>
    /// <param name="source">COM 物件。The COM object.</param>
    /// <param name="eventsIid">事件介面 IID。The event interface IID.</param>
    /// <param name="sink">接收器(實作該事件介面的 ComVisible 類別)。The sink.</param>
    /// <returns>連線,Dispose 時解除。The connection; dispose to detach.</returns>
    public static ComEventConnection Advise(object source, Guid eventsIid, object sink)
    {
        IConnectionPointContainer container = (IConnectionPointContainer)source;
        container.FindConnectionPoint(ref eventsIid, out IConnectionPoint? point);

        if (point is null)
        {
            throw new InvalidOperationException($"COM 物件沒有事件介面 {eventsIid} 的連接點。");
        }

        point.Advise(sink, out int cookie);
        return new ComEventConnection(point, cookie);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _point.Unadvise(_cookie);
    }
}
