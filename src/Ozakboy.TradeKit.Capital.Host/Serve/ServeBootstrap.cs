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
    /// <returns>結束碼。The exit code.</returns>
    public static async Task<int> RunAsync(
        ISkQuoteSource source,
        Func<Func<Task<int>>, Task<int>> dispatch,
        ServeTimings timings,
        Stream stdin,
        Stream stdout,
        Stream stderr)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        ServeLogin login = ServeLogin.ReadAndClear();
        ProtocolWriter output = new(stdout);
        using StreamWriter errorWriter = new(stderr, new UTF8Encoding(false));
        HostLog log = new(errorWriter, TimeProvider.System);
        ServeHost host = new(source, output, log, timings, TimeProvider.System);

        Thread reader = new(() => PumpInput(stdin, host)) { IsBackground = true, Name = "serve stdin" };
        reader.Start();

        try
        {
            return await dispatch(() => host.RunAsync(login)).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // 宿主最外層:任何缺陷都轉成 faulted + 結束碼 1,讓用戶端照崩潰處理。The outermost layer: a defect becomes faulted + exit 1.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            log.Write("宿主內部錯誤:" + exception);
            output.WriteStatus(ServeStates.Faulted, writer => writer.WriteString("reason", ServeStates.ReasonInternalError));
            return ServeExitCodes.InternalError;
        }
    }

    private static void PumpInput(Stream stdin, ServeHost host)
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

        host.CompleteInput();
    }
}
