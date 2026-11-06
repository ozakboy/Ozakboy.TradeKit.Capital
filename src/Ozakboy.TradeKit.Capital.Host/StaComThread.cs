using System.Windows.Forms;

namespace Ozakboy.TradeKit.Capital.Host;

/// <summary>
/// 專用的 STA 執行緒 + WinForms 訊息迴圈。SKCOM 的物件都在這條執行緒建立、呼叫,事件也從這裡的訊息迴圈送進來。
/// A dedicated STA thread running a WinForms message loop. Every SKCOM object is created and called here, and its
/// events arrive through this loop.
/// </summary>
internal sealed class StaComThread : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<SynchronizationContext> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SynchronizationContext? _context;
    private bool _disposed;

    /// <summary>
    /// 啟動執行緒並等訊息迴圈就緒。
    /// Starts the thread and waits until its message loop is ready.
    /// </summary>
    public StaComThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "SKCOM STA" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _context = _ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// 在 STA 執行緒上跑一段非同步工作(其中的 await 也會回到 STA 執行緒)。
    /// Runs asynchronous work on the STA thread; its awaits resume on the STA thread too.
    /// </summary>
    /// <typeparam name="T">結果型別。Result type.</typeparam>
    /// <param name="work">工作。The work.</param>
    /// <returns>工作的結果。The work's result.</returns>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);

        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _context!.Post(
            async _ =>
            {
                try
                {
                    completion.SetResult(await work().ConfigureAwait(true));
                }
#pragma warning disable CA1031 // 不是吞掉:任何例外都原封不動交給等待結果的呼叫端。Not swallowed: handed to the awaiting caller as is.
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
#pragma warning restore CA1031
            },
            null);

        return completion.Task;
    }

    /// <summary>結束訊息迴圈。Ends the message loop.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _context?.Post(_ => Application.ExitThread(), null);
        _thread.Join(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        using WindowsFormsSynchronizationContext context = new();
        SynchronizationContext.SetSynchronizationContext(context);
        _ready.SetResult(context);
        Application.Run();
    }
}
