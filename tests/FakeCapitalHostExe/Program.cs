using System.Globalization;
using FakeCapitalHostExe;
using Ozakboy.TradeKit.Capital.Host.Serve;

// 假群益宿主:與真宿主相同的 serve 協定(連結同一份 Serve\*.cs),SKCOM 由 FakeSkQuoteSource 模擬。
// 行為以環境變數控制,見 FakeSettings。
// The fake Capital host: the same serve protocol as the real host (linked from the same Serve\*.cs), with SKCOM
// simulated by FakeSkQuoteSource. Behaviour is controlled by environment variables; see FakeSettings.
if (args.Length == 0 || args[0] != "serve")
{
    Console.Error.WriteLine("usage: FakeCapitalHostExe serve");
    return 2;
}

FakeSettings settings = FakeSettings.FromEnvironment();

ServeTimings timings = new()
{
    ConnectTimeout = settings.ConnectTimeout,
    ProductsTimeout = settings.ProductsTimeout,
    KLineQuiet = settings.KLineQuiet,
    KLineNoData = settings.KLineNoData,
    KLinePoll = TimeSpan.FromMilliseconds(20),
    ReconnectDelays = settings.ReconnectDelays,
    DomesticReadyTimeout = settings.DomesticReadyTimeout,
    DomesticKLineTimeout = settings.DomesticKLineTimeout,
    OverseasOptionsReadyTimeout = settings.OverseasOptionsReadyTimeout,
    AccountQuiet = settings.AccountQuiet,
    AccountQuietAfterStatus = settings.AccountQuietAfterStatus,
    AccountPoll = TimeSpan.FromMilliseconds(20),
    ReportInterval = settings.ReportInterval,
};

using FakeSkQuoteSource source = new(settings);
using Stream stdin = Console.OpenStandardInput();
using Stream stdout = Console.OpenStandardOutput();
using Stream stderr = Console.OpenStandardError();

int exitCode = await ServeBootstrap.RunAsync(
        source,
        work => work(),
        timings,
        stdin,
        stdout,
        stderr,
        settings.ParentCheckInterval,
        settings.ExitGrace,
        code =>
        {
            // 強制結束也記一筆,測試看得出走的是哪條路。A forced exit is recorded too, so tests can tell which path ran.
            settings.Record(string.Create(CultureInfo.InvariantCulture, $"forced_exit {code}"));
            Environment.Exit(code);
        })
    .ConfigureAwait(false);
settings.Record(string.Create(CultureInfo.InvariantCulture, $"exit {exitCode}"));
return exitCode;
