namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>
/// 探針的結束保險:記完 <c>done</c> 之後若 15 秒內行程還沒結束(實測 2026-11-09:probe-full 寫完 done 後卡在解除 COM 連接點,
/// 掛著群益登入好幾天),就強制以同一個結束碼結束。serve 模式另有 <see cref="Serve.ExitGuard"/>。
/// The probe's exit guard. After "done" is written, the process is forced to end with the same code if it is still
/// alive 15 seconds later. A probe was seen hanging while it detached its COM connection points.
/// </summary>
internal static class ProbeExitGuard
{
    /// <summary>寬限時間。The grace period.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);

    private static int _armed;

    /// <summary>
    /// 啟動保險(只有第一次有效)。用背景執行緒計時,不依賴卡住的 STA 執行緒或訊息迴圈。
    /// Arms the guard once. A background thread keeps the time, so a stuck STA thread cannot block it.
    /// </summary>
    /// <param name="exitCode">強制結束時的結束碼。The exit code for a forced exit.</param>
    public static void Arm(int exitCode)
    {
        if (Interlocked.Exchange(ref _armed, 1) != 0)
        {
            return;
        }

        Thread watchdog = new(() =>
        {
            Thread.Sleep(Grace);
            ProbeLog.Say($"收尾超過 {Grace.TotalSeconds:0} 秒還沒結束,強制結束(結束碼 {exitCode})。");
            Environment.Exit(exitCode);
        })
        {
            IsBackground = true,
            Name = "Probe exit guard",
        };

        watchdog.Start();
    }
}
