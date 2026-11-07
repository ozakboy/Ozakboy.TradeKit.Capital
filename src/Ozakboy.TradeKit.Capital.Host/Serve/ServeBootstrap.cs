using System.Text;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// <c>serve</c> 的進入點骨架:讀環境變數(讀完即清)、接 stdin / stdout / stderr、起讀 stdin 的執行緒,再把
/// <see cref="ServeHost.RunAsync"/> 交給 <c>dispatch</c>(真宿主是 STA 執行緒,假宿主直接跑)。真宿主與假宿主共用這一份。
/// The serve entry skeleton: reads (and clears) the environment, wires stdin, stdout and stderr, starts the stdin reader
/// thread and hands <see cref="ServeHost.RunAsync"/> to <c>dispatch</c> (the STA thread in the real host, inline in the
/// fake). The real and the fake host share this.
/// </summary>
internal static class ServeBootstrap
{
    /// <summary>
    /// 跑 serve,回傳結束碼。
    /// Runs serve and returns the exit code.
    /// </summary>
    /// <param name="source">SKCOM(或假實作)。SKCOM or a fake.</param>
    /// <param name="dispatch">在哪條執行緒上跑協定層。Where the protocol layer runs.</param>
    /// <param name="timings">時限。Timings.</param>
    /// <param name="stdin">標準輸入。Standard input.</param>
    /// <param name="stdout">標準輸出。Standard output.</param>
    /// <param name="stderr">標準錯誤。Standard error.</param>
    /// <param name="parentCheckInterval">父行程看門狗的檢查間隔(預設 5 秒)。The parent watchdog interval, 5 s by default.</param>
    /// <param name="exitGrace">決定結束後給有序收尾的寬限(預設 15 秒),時間到仍未結束就強制結束。The grace before a forced exit, 15 s by default.</param>
    /// <param name="forceExit">強制結束(預設 <see cref="Environment.Exit(int)"/>)。The forced exit; Environment.Exit by default.</param>
    /// <returns>結束碼。The exit code.</returns>
    /// <remarks>
    /// 結束訊號有兩個,效果相同(LeaveMonitor 海外與國內後以 0 結束):stdin 讀到 EOF(父行程關掉管線或消失),以及父行程看門狗
    /// (<see cref="ParentWatch"/>)發現 <c>CAPITAL_PARENT_PID</c> 那個行程不在了。收到訊號後 <see cref="ExitGuard"/> 開始倒數,
    /// 協定層若因 STA 被卡住而無法收尾,寬限一到就強制結束,不留孤兒宿主。
    /// Two end signals with the same effect (leave both monitors, exit 0): EOF on stdin, and the parent watchdog finding
    /// the CAPITAL_PARENT_PID process gone. Either one starts the ExitGuard countdown, so a protocol layer stuck behind a
    /// blocked STA is ended forcibly once the grace runs out, leaving no orphaned host.
    /// </remarks>
    public static async Task<int> RunAsync(
        ISkQuoteSource source,
        Func<Func<Task<int>>, Task<int>> dispatch,
        ServeTimings timings,
        Stream stdin,
        Stream stdout,
        Stream stderr,
        TimeSpan? parentCheckInterval = null,
        TimeSpan? exitGrace = null,
        Action<int>? forceExit = null)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        ServeLogin login = ServeLogin.ReadAndClear();
        ProtocolWriter output = new(stdout);
        using StreamWriter errorWriter = new(stderr, new UTF8Encoding(false));
        HostLog log = new(errorWriter, TimeProvider.System);
        ServeHost host = new(source, output, log, timings, TimeProvider.System);

        // 結束保險刻意不釋放:協定層回傳之後還有 COM 收尾(解除連接點、結束 STA),那段卡住也要能強制結束。
        // The guard is deliberately not disposed: COM teardown still follows the protocol layer, and a hang there must be ended too.
#pragma warning disable CA2000 // 見上:要活到行程結束。See above: it must live until the process ends.
        ExitGuard guard = new(exitGrace ?? TimeSpan.FromSeconds(15), forceExit ?? Environment.Exit, log, TimeProvider.System);
#pragma warning restore CA2000

        void InputGone(string reason)
        {
            host.CompleteInput();
            guard.Arm(reason, ServeExitCodes.Success);
        }

        Thread reader = new(() => PumpInput(stdin, host, () => InputGone("stdin 已關閉"))) { IsBackground = true, Name = "serve stdin" };
        reader.Start();

        using ParentWatch? parentWatch = ParentWatch.Start(
            ParentWatch.ReadParentPid(),
            parentCheckInterval ?? TimeSpan.FromSeconds(5),
            () =>
            {
                log.Write("父行程已經不在,視同 stdin 結束。");
                InputGone("父行程不在");
            },
            log,
            TimeProvider.System);

        int exitCode;

        try
        {
            exitCode = await dispatch(() => host.RunAsync(login)).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // 宿主最外層:任何缺陷都轉成 faulted + 結束碼 1,讓用戶端照崩潰處理。The outermost layer: a defect becomes faulted + exit 1.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            log.Write("宿主內部錯誤:" + exception);
            output.WriteStatus(ServeStates.Faulted, writer => writer.WriteString("reason", ServeStates.ReasonInternalError));
            exitCode = ServeExitCodes.InternalError;
        }

        guard.Arm("協定層已結束", exitCode);
        return exitCode;
    }

    private static void PumpInput(Stream stdin, ServeHost host, Action ended)
    {
        try
        {
            using StreamReader reader = new(stdin, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);

            while (reader.ReadLine() is { } line)
            {
                host.EnqueueRequest(line);
            }
        }
        catch (IOException)
        {
            // 管線被關掉,視同輸入結束。A broken pipe counts as the end of input.
        }
        catch (ObjectDisposedException)
        {
            // 同上。Same as above.
        }

        ended();
    }
}
