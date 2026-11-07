using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ozakboy.TradeKit.Capital.Protocol;
using Ozakboy.TradeKit.Capital.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Tests;

// 國內期貨歷史 K 線與宿主的壽命(stdin EOF、父行程看門狗、Job Object),對假宿主跑。假宿主的國內行為照 2026-11-07 probe-tw 實測,
// 見 tests/FakeCapitalHostExe/FakeDomesticMarket.cs。
// Domestic futures K-lines and the host's lifetime (stdin EOF, parent watchdog, Job Object) against the fake host, whose
// domestic behaviour follows the 2026-11-07 probe-tw measurements.
public sealed partial class CapitalHostSessionTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);

    [TestMethod]
    public async Task DomesticFullSessionIncludesThePreviousNightAndKeepsEmptyBars()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        // 國內在海外就緒後才連(約 0.5 秒);這時送的請求由宿主等到國內就緒。Sent while the domestic side is still connecting.
        IReadOnlyList<CapitalDomesticBar> bars = (await rig.Session.GetDomesticKLinesAsync("TX00", CapitalDomesticKLineType.Minute, CapitalTradeSession.Full, Monday, Monday)).GetValueOrThrow();

        Assert.AreEqual(CapitalDomesticState.Connecting, rig.DomesticChanges.First().Current, "connecting came first");
        Assert.AreEqual(840 + 300, bars.Count, "Friday's night session (15:01–05:00) plus Monday's day session (08:46–13:45)");
        Assert.IsTrue(bars.All(bar => bar.Code == "TX00"), "the stray MTX00 row is dropped");
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.Zero), bars[0].OpenTimeUtc, "Friday 15:01 Taipei is the end of the first bar");
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 15, 59, 0, TimeSpan.Zero), bars[539].OpenTimeUtc, "the 00:00 bar is dated on the new day and opens at 23:59");
        Assert.AreEqual(new DateTimeOffset(2026, 11, 2, 5, 44, 0, TimeSpan.Zero), bars[^1].OpenTimeUtc, "the last bar ends at 13:45");
        Assert.AreEqual(39786.00m, bars[0].Open);
        Assert.AreEqual(0L, bars[5].Volume, "empty bars are returned as they are");
        Assert.IsTrue(bars.All(bar => bar.TradingDay is null));
        Assert.AreEqual(CapitalDomesticState.Ready, rig.Session.DomesticState);
        StringAssert.Contains(rig.CallLog(), "tw_kline TX00 0 1 0 20261102 20261102 1");
    }

    [TestMethod]
    public async Task DomesticAmSessionHasTheDaySessionOnly()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        IReadOnlyList<CapitalDomesticBar> bars = (await rig.Session.GetDomesticKLinesAsync("MTX00", CapitalDomesticKLineType.Minute, CapitalTradeSession.AmOnly, Monday, Monday)).GetValueOrThrow();

        Assert.AreEqual(300, bars.Count);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 2, 0, 45, 0, TimeSpan.Zero), bars[0].OpenTimeUtc, "08:46 Taipei is the end of the first bar");
    }

    [TestMethod]
    public async Task DomesticDailyBarsAreDatedByTradingDay()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        IReadOnlyList<CapitalDomesticBar> bars = (await rig.Session.GetDomesticKLinesAsync("TM0000", CapitalDomesticKLineType.Day, CapitalTradeSession.Full, Monday, new DateOnly(2026, 11, 7))).GetValueOrThrow();

        CollectionAssert.AreEqual(
            new DateOnly?[] { new(2026, 11, 2), new(2026, 11, 3), new(2026, 11, 4), new(2026, 11, 5), new(2026, 11, 6) },
            bars.Select(bar => bar.TradingDay).ToArray(),
            "Saturday is not a trading day");
        Assert.IsTrue(bars.All(bar => bar.OpenTimeUtc is null && bar.Type == CapitalDomesticKLineType.Day));
        StringAssert.Contains(rig.CallLog(), "tw_kline TM0000 4 1 0 20261102 20261107 1");
    }

    // 依據:實測一個請求最多 60,000 筆,超過保留最新、截掉最舊;這裡把假宿主的上限調成 100。
    [TestMethod]
    public async Task OverTheRowCapCapitalKeepsTheNewestBars()
    {
        await using Rig rig = Rig.Create(environment: new() { ["FAKE_CAPITAL_TW_KLINE_LIMIT"] = "100" });
        await rig.StartAsync();

        IReadOnlyList<CapitalDomesticBar> bars = (await rig.Session.GetDomesticKLinesAsync("TX00", CapitalDomesticKLineType.Minute, CapitalTradeSession.Full, Monday, new DateOnly(2026, 11, 3))).GetValueOrThrow();

        Assert.AreEqual(100, bars.Count);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 3, 5, 44, 0, TimeSpan.Zero), bars[^1].OpenTimeUtc, "the newest bar is kept");
        Assert.AreEqual(new DateTimeOffset(2026, 11, 3, 4, 5, 0, TimeSpan.Zero), bars[0].OpenTimeUtc, "the oldest are dropped, without an error");
    }

    [TestMethod]
    [DataRow("2330", 9999)]
    [DataRow("TX00AM", 3023)]
    public async Task AnUnknownDomesticCodeIsRejectedWithCapitalsCode(string code, int expected)
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        Error error = (await rig.Session.GetDomesticKLinesAsync(code, CapitalDomesticKLineType.Minute, CapitalTradeSession.Full, Monday, Monday)).Failure();

        Assert.AreEqual(CapitalErrorCodes.KLineRejected, error.Code);
        Assert.IsTrue(error.TryGetInt64(CapitalErrorDataKeys.Code, out long reported));
        Assert.AreEqual(expected, reported);
        Assert.IsTrue(error.TryGetData(CapitalErrorDataKeys.Symbol, out string? symbol));
        Assert.AreEqual(code, symbol);
    }

    [TestMethod]
    public async Task AMissingCompleteEventIsAKLineTimeout()
    {
        await using Rig rig = Rig.Create(environment: new()
        {
            ["FAKE_CAPITAL_TW_NO_COMPLETE"] = "1",
            ["FAKE_CAPITAL_TW_KLINE_TIMEOUT_MS"] = "500",
        });
        await rig.StartAsync();

        Error error = (await rig.Session.GetDomesticKLinesAsync("TX00", CapitalDomesticKLineType.Minute, CapitalTradeSession.AmOnly, Monday, Monday)).Failure();

        Assert.AreEqual(CapitalErrorCodes.KLineTimeout, error.Code);
        Assert.AreEqual(ErrorCategory.Timeout, error.Category);
        Assert.IsTrue(error.TryGetInt64("received", out long received));
        Assert.AreEqual(300L, received);
    }

    [TestMethod]
    public async Task DomesticUnavailableLeavesOverseasReadyAndFailsDomesticRequestsExplicitly()
    {
        await using Rig rig = Rig.Create(environment: new() { ["FAKE_CAPITAL_TW_ENTER_CODE"] = "9999" });
        await rig.StartAsync();

        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.Session.DomesticState == CapitalDomesticState.Unavailable, Wait), "unavailable");
        Assert.AreEqual(CapitalDomesticReasons.EnterRejected, rig.Session.DomesticReason);
        CapitalDomesticStateChangedEventArgs change = rig.DomesticChanges.Last();
        Assert.AreEqual(9999, change.Code);
        Assert.AreEqual(CapitalHostState.Ready, rig.Session.State, "overseas is unaffected");

        Error error = (await rig.Session.GetDomesticKLinesAsync("TX00", CapitalDomesticKLineType.Minute, CapitalTradeSession.Full, Monday, Monday)).Failure();
        Assert.AreEqual(CapitalErrorCodes.DomesticUnavailable, error.Code);
        Assert.AreEqual(ErrorCategory.Unavailable, error.Category);
        Assert.IsTrue(error.TryGetData(CapitalErrorDataKeys.Reason, out string? reason));
        Assert.AreEqual(CapitalDomesticReasons.EnterRejected, reason);
        Assert.IsTrue(error.TryGetData(CapitalErrorDataKeys.DomesticState, out string? domestic));
        Assert.AreEqual("unavailable", domestic);

        Assert.IsTrue((await rig.Session.GetKLinesAsync(UsdJpy, CapitalKLineType.Minute, Monday, new DateOnly(2026, 11, 5), 2)).IsSuccess, "overseas K-lines still work");
        CapitalConnectionStatus status = (await rig.Session.GetConnectionStatusAsync()).GetValueOrThrow();
        Assert.AreEqual(CapitalDomesticState.Unavailable, status.Domestic);
        Assert.AreEqual(CapitalDomesticReasons.EnterRejected, status.DomesticReason);
    }

    [TestMethod]
    public async Task ADomesticOpenFailureIsUnavailable()
    {
        await using Rig rig = Rig.Create(environment: new() { ["FAKE_CAPITAL_TW_OPEN_HRESULT"] = "0x80040154" });
        await rig.StartAsync();

        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.Session.DomesticState == CapitalDomesticState.Unavailable, Wait));
        Assert.AreEqual(CapitalDomesticReasons.SkComNotRegistered, rig.Session.DomesticReason);
        Assert.IsFalse(rig.CallLog().Contains("tw_enter", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ADomesticDisconnectIsReconnectedWithoutTouchingTheOverseasState()
    {
        await using Rig rig = Rig.Create(environment: new()
        {
            ["FAKE_CAPITAL_TW_DISCONNECT_AFTER_MS"] = "300",
            ["FAKE_CAPITAL_TW_READY_DELAY_MS"] = "50",
            ["FAKE_CAPITAL_RECONNECT_DELAYS_MS"] = "50,100",
        });
        await rig.StartAsync();

        Assert.IsTrue(
            await FakeHost.WaitUntilAsync(() => rig.DomesticChanges.Any(e => e.Current == CapitalDomesticState.Disconnected) && rig.Session.DomesticState == CapitalDomesticState.Ready, Wait),
            "disconnected, then ready again");

        CapitalDomesticStateChangedEventArgs lost = rig.DomesticChanges.First(e => e.Current == CapitalDomesticState.Disconnected);
        Assert.AreEqual(CapitalDomesticReasons.ConnectionLost, lost.Reason);
        CollectionAssert.AreEqual(
            new[] { CapitalDomesticState.Connecting, CapitalDomesticState.Ready, CapitalDomesticState.Disconnected, CapitalDomesticState.Ready },
            rig.DomesticChanges.Select(e => e.Current).ToArray());
        Assert.IsFalse(rig.States.Contains(CapitalHostState.Disconnected), "StateChanged never saw the domestic disconnect");

        string[] lines = rig.CallLogLines();
        Assert.AreEqual(2, lines.Count(line => line.Contains(" tw_enter ", StringComparison.Ordinal)));
        Assert.AreEqual(1, lines.Count(line => line.Contains(" enter ", StringComparison.Ordinal)), "the overseas monitor is not re-entered");
        Assert.AreEqual(1, lines.Count(line => line.Contains(" login ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task StopResetsTheDomesticState()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();
        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.Session.DomesticState == CapitalDomesticState.Ready, Wait));
        Assert.AreEqual(CapitalDomesticState.Ready, (await rig.Session.GetConnectionStatusAsync()).GetValueOrThrow().Domestic);

        _ = await rig.Session.StopAsync();

        Assert.AreEqual(CapitalDomesticState.NotStarted, rig.Session.DomesticState);
        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.CallLog().Contains(" tw_leave", StringComparison.Ordinal), Wait), "the domestic monitor was left");
    }

    [TestMethod]
    public async Task DomesticArgumentsAreCheckedBeforeAnythingIsSent()
    {
        await using Rig rig = Rig.Create();

        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetDomesticKLinesAsync("TX,00", CapitalDomesticKLineType.Minute, CapitalTradeSession.Full, Monday, Monday)).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetDomesticKLinesAsync("TX00", (CapitalDomesticKLineType)1, CapitalTradeSession.Full, Monday, Monday)).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetDomesticKLinesAsync("TX00", CapitalDomesticKLineType.Minute, (CapitalTradeSession)5, Monday, Monday)).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetDomesticKLinesAsync("TX00", CapitalDomesticKLineType.Minute, CapitalTradeSession.Full, Monday, Monday.AddDays(-1))).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.HostNotReady, (await rig.Session.GetDomesticKLinesAsync("TX00", CapitalDomesticKLineType.Minute, CapitalTradeSession.Full, Monday, Monday)).Failure().Code);
        Assert.AreEqual(string.Empty, rig.CallLog(), "no process was started");
    }

    // ── 宿主壽命(實機發現孤兒宿主之後補上)。The host's lifetime, added after an orphaned host was found on a real machine. ──

    [TestMethod]
    public async Task TheHostIsPutIntoTheSessionsJobObject()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        Assert.IsTrue(rig.Session.HostIsInJob(), "the host joined the KILL_ON_JOB_CLOSE job");
    }

    [TestMethod]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task ClosingTheJobEndsTheHostEvenIfItIgnoresEverythingElse()
    {
        string callLog = NewCallLog();
        using Process host = StartFakeHost(callLog, parentPid: null);
        await ReadUntilAsync(host, line => line.Contains("\"state\":\"ready\"", StringComparison.Ordinal));

        CapitalHostJob? job = CapitalHostJob.TryCreate(out string problem);
        Assert.IsNotNull(job, problem);
        Assert.IsTrue(job.TryAssign(host, out problem), problem);
        Assert.IsTrue(job.Contains(host));

        // stdin 還開著(宿主不會自己結束);只關 job handle。stdin stays open; only the job handle is closed.
        job.Dispose();

        Assert.IsTrue(host.WaitForExit(TimeSpan.FromSeconds(10)), "the system ended the host when the job closed");
        TryDelete(callLog);
    }

    [TestMethod]
    public async Task TheHostLeavesBothMonitorsAndExitsZeroWhenStdinCloses()
    {
        string callLog = NewCallLog();
        using Process host = StartFakeHost(callLog, parentPid: null);
        await ReadUntilAsync(host, line => line.Contains("\"domestic\":\"ready\"", StringComparison.Ordinal));

        host.StandardInput.Close();

        Assert.IsTrue(host.WaitForExit(TimeSpan.FromSeconds(10)), "stdin EOF ends the host");
        Assert.AreEqual(0, host.ExitCode);
        string log = ReadCallLog(callLog);
        StringAssert.Contains(log, " leave");
        StringAssert.Contains(log, " tw_leave");
        Assert.IsFalse(log.Contains("forced_exit", StringComparison.Ordinal), "the orderly path ran, not the safety net");
        TryDelete(callLog);
    }

    [TestMethod]
    public async Task TheHostEndsWhenItsParentIsGoneEvenWithStdinStillOpen()
    {
        string parentLog = NewCallLog();
        string childLog = NewCallLog();

        // 父行程的替身:另一個假宿主(不會自己結束)。A stand-in parent: another fake host that never ends by itself.
        using Process parent = StartFakeHost(parentLog, parentPid: null);
        await ReadUntilAsync(parent, line => line.Contains("\"state\":\"ready\"", StringComparison.Ordinal));
        using Process child = StartFakeHost(childLog, parent.Id, new() { ["FAKE_CAPITAL_PARENT_CHECK_MS"] = "200" });
        await ReadUntilAsync(child, line => line.Contains("\"domestic\":\"ready\"", StringComparison.Ordinal));

        parent.Kill(entireProcessTree: true);

        Assert.IsTrue(child.WaitForExit(TimeSpan.FromSeconds(10)), "the watchdog ended the orphan");
        Assert.AreEqual(0, child.ExitCode);
        string log = ReadCallLog(childLog);
        StringAssert.Contains(log, " leave");
        StringAssert.Contains(log, " tw_leave");
        TryDelete(parentLog);
        TryDelete(childLog);
    }

    [TestMethod]
    public async Task AParentThatIsAlreadyGoneEndsTheHostRightAway()
    {
        string callLog = NewCallLog();
        int gone;

        using (Process shortLived = StartFakeHost(NewCallLog(), parentPid: null))
        {
            shortLived.Kill(entireProcessTree: true);
            _ = shortLived.WaitForExit(TimeSpan.FromSeconds(5));
            gone = shortLived.Id;
        }

        using Process host = StartFakeHost(callLog, gone);

        Assert.IsTrue(host.WaitForExit(TimeSpan.FromSeconds(15)), "ends without waiting for anything");
        Assert.AreEqual(0, host.ExitCode);
        await Task.CompletedTask;
        TryDelete(callLog);
    }

    private static string NewCallLog() => Path.Combine(Path.GetTempPath(), "capital-calls-" + Guid.NewGuid().ToString("N")[..8] + ".log");

    private static Process StartFakeHost(string callLog, int? parentPid, Dictionary<string, string>? extra = null)
    {
        ProcessStartInfo startInfo = new(FakeHost.ExecutablePath())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };

        startInfo.ArgumentList.Add("serve");
        startInfo.Environment["CAPITAL_LOGIN_ID"] = FakeHost.LoginId;
        startInfo.Environment["CAPITAL_PASSWORD"] = FakeHost.Password;
        startInfo.Environment["FAKE_CAPITAL_CALL_LOG"] = callLog;
        startInfo.Environment["FAKE_CAPITAL_LIVE_TICK_MS"] = "0";
        startInfo.Environment.Remove("CAPITAL_PARENT_PID");

        if (parentPid is { } pid)
        {
            startInfo.Environment["CAPITAL_PARENT_PID"] = pid.ToString(CultureInfo.InvariantCulture);
        }

        foreach ((string key, string value) in extra ?? [])
        {
            startInfo.Environment[key] = value;
        }

        Process process = Process.Start(startInfo)!;
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task ReadUntilAsync(Process process, Func<string, bool> match)
    {
        using CancellationTokenSource timeout = new(Wait);

        while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
        {
            if (match(line))
            {
                return;
            }
        }

        Assert.Fail("the host ended before the expected line");
    }

    private static string ReadCallLog(string path)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            catch (IOException)
            {
                Thread.Sleep(10);
            }
        }

        return File.ReadAllText(path);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 假宿主可能還握著檔案。The fake may still hold the file.
        }
    }
}
