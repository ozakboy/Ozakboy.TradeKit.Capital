using Ozakboy.TradeKit.Capital.Host.Interop;
using Ozakboy.TradeKit.Capital.Host.Serve;

namespace Ozakboy.TradeKit.Capital.Host;

/// <summary>
/// <c>serve</c> 指令:在專用 STA 執行緒上以真 SKCOM 跑 <see cref="ServeHost"/>。
/// The serve command: runs <see cref="ServeHost"/> with the real SKCOM on a dedicated STA thread.
/// </summary>
internal static class ServeCommand
{
    /// <summary>
    /// 執行,回傳結束碼(見 <see cref="ServeExitCodes"/>)。
    /// Runs and returns the exit code (see <see cref="ServeExitCodes"/>).
    /// </summary>
    /// <returns>結束碼。The exit code.</returns>
    public static async Task<int> RunAsync()
    {
        using StaComThread sta = new();
        using Stream stdin = Console.OpenStandardInput();
        using Stream stdout = Console.OpenStandardOutput();
        using Stream stderr = Console.OpenStandardError();

        // COM 物件要在建立它的 STA 上解除連接點,所以 Dispose 放在下方 finally 的 STA 委派裡,不用 using。
        // The connection points must be released on the STA that created them, so Dispose runs inside the STA delegate
        // in the finally below rather than through a using.
#pragma warning disable CA2000
        SkComQuoteSource source = new();
#pragma warning restore CA2000

        try
        {
            return await ServeBootstrap.RunAsync(source, work => sta.InvokeAsync(work), new ServeTimings(), stdin, stdout, stderr)
                .ConfigureAwait(false);
        }
        finally
        {
            _ = await sta.InvokeAsync(() =>
                {
                    source.Dispose();
                    return Task.FromResult(0);
                })
                .ConfigureAwait(false);
        }
    }
}
