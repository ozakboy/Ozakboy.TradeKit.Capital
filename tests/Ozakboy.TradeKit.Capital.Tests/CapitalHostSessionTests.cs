using System.Collections.Concurrent;
using Ozakboy.Security.Masking;
using Ozakboy.TradeKit.Capital.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Tests;

/// <summary>
/// 以假宿主(tests\FakeCapitalHostExe:真宿主的協定層 + 依實測模擬的 SKCOM)驅動 <see cref="CapitalHostSession"/>。
/// Drives <see cref="CapitalHostSession"/> against the fake host: the real host's protocol layer with a simulated SKCOM.
/// </summary>
[TestClass]
public sealed class CapitalHostSessionTests
{
    private static readonly CapitalSymbol UsdJpy = new("FX", "SUSDJPY");
    private static readonly CapitalSymbol Nq = new("CME", "NQ0000");

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [ClassCleanup]
    public static void KillLeftovers() => FakeHost.KillLeftovers();

    [TestMethod]
    public async Task Start_LogsInThroughTheEnvironment_ClearsIt_AndBecomesReady()
    {
        await using Rig rig = Rig.Create(environment: new()
        {
            // 假宿主只在環境變數送來的帳密與預期相符時才登入成功。The fake logs in only if the environment carried these.
            ["FAKE_CAPITAL_EXPECT_ID"] = FakeHost.LoginId,
            ["FAKE_CAPITAL_EXPECT_PASSWORD"] = FakeHost.Password,
        });

        Result result = await rig.Session.StartAsync(Rig.Credentials());

        Assert.IsTrue(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        Assert.AreEqual(CapitalHostState.Ready, rig.Session.State);
        CollectionAssert.AreEqual(
            new[] { CapitalHostState.Starting, CapitalHostState.LoggingIn, CapitalHostState.Connecting, CapitalHostState.Ready },
            rig.States.ToArray());
        StringAssert.Contains(rig.CallLog(), "login env_cleared=True", "the host must clear the variables right after reading");
    }

    [TestMethod]
    public async Task Subscribe_ReplaysTheDayThenGoesLive_WithDecimalPricesAndUtcTimes()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        Result<IReadOnlyList<CapitalSymbol>> subscribed = await rig.Session.SubscribeAsync([UsdJpy, Nq, UsdJpy]);

        CollectionAssert.AreEqual(new[] { UsdJpy, Nq }, subscribed.GetValueOrThrow().ToArray());

        List<CapitalMarketEvent> events = await rig.ReadUntilAsync(list =>
            list.OfType<CapitalTick>().Any(t => t.Symbol == UsdJpy && !t.IsReplay) && list.OfType<CapitalQuote>().Any(q => q.Symbol == Nq));

        CapitalTick[] jpy = [.. events.OfType<CapitalTick>().Where(t => t.Symbol == UsdJpy)];
        CapitalTick lastReplay = jpy.Last(t => t.IsReplay);

        // 依據:ticks.txt 索引 137 的 20261105 224357(FX 美東)→ 2026-11-06T03:43:57Z,價格 15784 ÷ 10^2。
        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 3, 43, 57, TimeSpan.Zero), lastReplay.TimeUtc);
        Assert.AreEqual(157.84m, lastReplay.Price);
        Assert.AreEqual(57842L, lastReplay.Sequence);
        Assert.IsTrue(jpy.TakeWhile(t => t.IsReplay).Count() >= 3, "the replay comes first");
        Assert.IsTrue(jpy.Select(t => t.Sequence).Zip(jpy.Skip(1).Select(t => t.Sequence)).All(pair => pair.First < pair.Second), "nPtr increases");

        CapitalTick nqReplay = events.OfType<CapitalTick>().Last(t => t.Symbol == Nq && t.IsReplay);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 3, 43, 56, TimeSpan.Zero), nqReplay.TimeUtc, "CME is Chicago time");

        CapitalQuote nqQuote = events.OfType<CapitalQuote>().First(q => q.Symbol == Nq);
        Assert.AreEqual(2, nqQuote.Decimals);
        Assert.AreEqual(26640.25m, nqQuote.Open);
        Assert.AreEqual(new DateOnly(2026, 11, 6), nqQuote.TradingDay);
        Assert.AreEqual(0L, rig.Session.DroppedEvents);
    }

    [TestMethod]
    public async Task GetProducts_MergesTheListAndTheDetails()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        IReadOnlyList<CapitalProduct> products = (await rig.Session.GetProductsAsync()).GetValueOrThrow();

        Assert.AreEqual(8, products.Count, "every row before ##");
        CapitalProduct twd = products.Single(p => p.Symbol == new CapitalSymbol("FX", "SUSDTWD"));
        Assert.AreEqual("新台幣", twd.Name);
        Assert.AreEqual(2, twd.Kind);
        Assert.IsNull(twd.LastTradeDate);
        Assert.IsNull(twd.FirstNoticeDate);

        CapitalProduct gold = products.Single(p => p.Symbol == new CapitalSymbol("NYM", "GC0000"));
        Assert.AreEqual(new DateOnly(2026, 11, 30), gold.FirstNoticeDate);
        Assert.AreEqual("GC_202612", gold.OrderCode);
        Assert.AreEqual(5, gold.Kind);
    }

    [TestMethod]
    public async Task GetKLines_EndsBySilence_KeepsOnlyTheAskedCode_AndConvertsTimes()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        IReadOnlyList<CapitalBar> minutes = (await rig.Session.GetKLinesAsync(UsdJpy, CapitalKLineType.Minute, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 5), 2)).GetValueOrThrow();

        Assert.AreEqual(3, minutes.Count, "the stray SEURUSD row is dropped");
        Assert.AreEqual(new DateTimeOffset(2026, 11, 2, 21, 0, 0, TimeSpan.Zero), minutes[0].OpenTimeUtc);
        Assert.AreEqual(157.89m, minutes[0].Open);
        Assert.IsTrue(minutes.All(bar => bar.Symbol == UsdJpy));

        IReadOnlyList<CapitalBar> days = (await rig.Session.GetKLinesAsync(UsdJpy, CapitalKLineType.Day, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 5), 2)).GetValueOrThrow();

        Assert.AreEqual(new DateOnly(2026, 10, 7), days[0].TradingDay);
        Assert.IsNull(days[0].OpenTimeUtc);
    }

    [TestMethod]
    public async Task GetKLines_MinuteBarsOfAnExchangeWithoutATimeZone_FailLocally()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        Result<IReadOnlyList<CapitalBar>> result = await rig.Session.GetKLinesAsync(new CapitalSymbol("INDEX", "NI225"), CapitalKLineType.Minute, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 5), 2);

        Assert.AreEqual(CapitalErrorCodes.UnknownExchange, result.Failure().Code);
        Assert.IsFalse(rig.CallLog().Contains("kline INDEX", StringComparison.Ordinal), "nothing was sent");
    }

    [TestMethod]
    public async Task GetConnectionStatus_ReportsTheMeasuredValues()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        CapitalConnectionStatus status = (await rig.Session.GetConnectionStatusAsync()).GetValueOrThrow();

        Assert.IsTrue(status.Connected);
        Assert.AreEqual(0, status.Connections);
        Assert.IsFalse(status.OutLimit);
    }

    [TestMethod]
    [DataRow(300, false)]
    [DataRow(307, true)]
    public async Task LoginFailure_IsReportedOnce_AndNeverRestarted(int code, bool locked)
    {
        await using Rig rig = Rig.Create(environment: new() { ["FAKE_CAPITAL_LOGIN_CODE"] = code.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        Result result = await rig.Session.StartAsync(Rig.Credentials());

        Assert.AreEqual(CapitalErrorCodes.LoginFailed, result.Failure().Code);
        Assert.IsTrue(result.Failure().TryGetInt64(CapitalErrorDataKeys.Code, out long reported));
        Assert.AreEqual(code, reported);
        Assert.IsTrue(result.Failure().TryGetBoolean(CapitalErrorDataKeys.Locked, out bool isLocked));
        Assert.AreEqual(locked, isLocked);
        Assert.AreEqual(CapitalHostState.LoginFailed, rig.Session.State);

        await Task.Delay(1500);
        Assert.AreEqual(1, rig.CallLogLines().Count(line => line.Contains(" login ", StringComparison.Ordinal)), "Login must never be retried");
        Assert.AreEqual(CapitalHostState.LoginFailed, rig.Session.State);
    }

    [TestMethod]
    [DataRow("0x80040154", CapitalErrorCodes.SkComNotRegistered)]
    [DataRow("0x8007007E", CapitalErrorCodes.SkComFileMissing)]
    [DataRow("0x80004005", CapitalErrorCodes.SkComCreateFailed)]
    public async Task SkComUnavailable_IsFaulted_WithItsHResult(string hresult, string expectedCode)
    {
        await using Rig rig = Rig.Create(environment: new() { ["FAKE_CAPITAL_OPEN_HRESULT"] = hresult });

        Result result = await rig.Session.StartAsync(Rig.Credentials());

        Assert.AreEqual(expectedCode, result.Failure().Code);
        Assert.IsTrue(result.Failure().TryGetData(CapitalErrorDataKeys.HResult, out string? reported));
        Assert.AreEqual(hresult, reported, ignoreCase: true);
        Assert.AreEqual(CapitalHostState.Faulted, rig.Session.State);
        Assert.IsFalse(rig.CallLog().Contains(" login ", StringComparison.Ordinal), "Login is never reached");
    }

    [TestMethod]
    public async Task NoConnectEvent_IsAConnectTimeout()
    {
        await using Rig rig = Rig.Create(environment: new()
        {
            ["FAKE_CAPITAL_NO_CONNECT"] = "1",
            ["FAKE_CAPITAL_CONNECT_TIMEOUT_MS"] = "300",
        });

        Result result = await rig.Session.StartAsync(Rig.Credentials());

        Assert.AreEqual(CapitalErrorCodes.ConnectTimeout, result.Failure().Code);
        Assert.AreEqual(CapitalHostState.Faulted, rig.Session.State);
    }

    [TestMethod]
    public async Task ACrash_IsRestartedWithBackoff_AndTheSubscriptionsAreResent()
    {
        string marker = Path.Combine(Path.GetTempPath(), "capital-crash-" + Guid.NewGuid().ToString("N")[..8]);

        await using Rig rig = Rig.Create(environment: new()
        {
            ["FAKE_CAPITAL_CRASH_AFTER_MS"] = "300",
            ["FAKE_CAPITAL_CRASH_ONCE_FILE"] = marker,
        });
        rig.Options.RestartDelays = [TimeSpan.FromMilliseconds(200)];

        try
        {
            await rig.StartAsync();
            _ = (await rig.Session.SubscribeAsync([UsdJpy])).GetValueOrThrow();

            Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.States.Count(s => s == CapitalHostState.Ready) >= 2, Wait), "restarted to Ready");

            string[] stockCalls = [.. rig.CallLogLines().Where(line => line.Contains(" stocks FX,SUSDJPY", StringComparison.Ordinal))];
            Assert.AreEqual(2, stockCalls.Length, "subscribed once per process");
            Assert.AreNotEqual(stockCalls[0].Split(' ')[0], stockCalls[1].Split(' ')[0], "the second subscription came from the restarted process");
            Assert.IsTrue(rig.CallLog().Contains(" crash", StringComparison.Ordinal));
            CollectionAssert.Contains(rig.States.SkipWhile(s => s != CapitalHostState.Ready).ToArray(), CapitalHostState.Starting, "Ready → Starting → … → Ready");

            // 重啟後的回補再送一次(nPtr 相同,可去重)。The replay arrives again after the restart, with the same nPtr.
            Assert.IsTrue(await FakeHost.WaitUntilAsync(
                () => rig.Drain().OfType<CapitalTick>().Count(t => t.Symbol == UsdJpy && t.Sequence == 57842) >= 2,
                Wait));

            // 崩潰後的重啟會重新登入(那是新行程);登入「失敗」才絕不重試。A restart logs in again; only a failed login is never retried.
            Assert.AreEqual(2, rig.CallLogLines().Count(line => line.Contains(" login ", StringComparison.Ordinal)));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [TestMethod]
    public async Task RepeatedCrashes_StopAfterMaxRestarts_InFaulted()
    {
        await using Rig rig = Rig.Create(environment: new() { ["FAKE_CAPITAL_CRASH_AFTER_MS"] = "100" });
        rig.Options.RestartDelays = [TimeSpan.FromMilliseconds(50)];
        rig.Options.MaxRestarts = 2;

        await rig.StartAsync();
        _ = await rig.Session.SubscribeAsync([UsdJpy]);

        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.Session.State == CapitalHostState.Faulted, Wait), "gives up in Faulted");
        Assert.AreEqual(3, rig.CallLogLines().Count(line => line.EndsWith(" crash", StringComparison.Ordinal)), "the first run plus two restarts");
    }

    [TestMethod]
    public async Task ADisconnect_IsReconnectedByTheHost_WhichResendsTheSubscriptions()
    {
        await using Rig rig = Rig.Create(environment: new()
        {
            ["FAKE_CAPITAL_DISCONNECT_AFTER_MS"] = "400",
            ["FAKE_CAPITAL_RECONNECT_DELAYS_MS"] = "50,100",
            ["FAKE_CAPITAL_RECONNECT_FAILURES"] = "1",
            ["FAKE_CAPITAL_CONNECT_TIMEOUT_MS"] = "300",
        });

        await rig.StartAsync();
        _ = (await rig.Session.SubscribeAsync([UsdJpy])).GetValueOrThrow();

        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.States.Contains(CapitalHostState.Disconnected), Wait), "Disconnected");
        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.Session.State == CapitalHostState.Ready && rig.States.Contains(CapitalHostState.Disconnected), Wait), "back to Ready");

        string[] lines = rig.CallLogLines();
        Assert.AreEqual(3, lines.Count(line => line.Contains(" enter ", StringComparison.Ordinal)), "the first connect, one silent retry, then success");
        Assert.AreEqual(2, lines.Count(line => line.Contains(" stocks FX,SUSDJPY", StringComparison.Ordinal)), "the host re-sent the subscription");
        Assert.AreEqual(1, lines.Count(line => line.Contains(" login ", StringComparison.Ordinal)), "a reconnect never logs in again");
        Assert.AreEqual(1, lines.Select(line => line.Split(' ')[0]).Distinct().Count(), "the same process throughout");
    }

    [TestMethod]
    public async Task Credentials_NeverAppearInLogsOrErrors()
    {
        await using Rig rig = Rig.Create(environment: new()
        {
            // 假宿主的公告與最後紀錄都含登入帳號(群益的紀錄可能如此);宿主與用戶端都必須遮掉。
            // The fake's announcement and last log contain the login ID, as Capital's may; both sides must mask it.
            ["FAKE_CAPITAL_ANNOUNCE_ID"] = "1",
            ["FAKE_CAPITAL_LOGIN_CODE"] = "300",
        });

        Result result = await rig.Session.StartAsync(Rig.Credentials());
        Assert.IsTrue(result.IsFailure);
        await Task.Delay(300);

        string everything = rig.Logger.AllText + "\n" + result.Error + "\n" + result.Error.Message + "\n"
            + string.Join('\n', (result.Error.Data ?? new Dictionary<string, string>()).Select(pair => pair.Key + "=" + pair.Value));

        Assert.IsFalse(everything.Contains(FakeHost.LoginId, StringComparison.OrdinalIgnoreCase), everything);
        Assert.IsFalse(everything.Contains(FakeHost.Password, StringComparison.Ordinal), everything);
        StringAssert.Contains(rig.Logger.AllText, "<ID>", "the host redacted the ID in its announcement");
    }

    [TestMethod]
    public async Task Stop_ShutsTheHostDownCleanly()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        Result stopped = await rig.Session.StopAsync();

        Assert.IsTrue(stopped.IsSuccess);
        Assert.AreEqual(CapitalHostState.Stopped, rig.Session.State);
        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.CallLog().Contains(" exit 0", StringComparison.Ordinal), Wait), "exit code 0 after shutdown");
        StringAssert.Contains(rig.CallLog(), " leave");
        Assert.AreEqual(CapitalErrorCodes.HostNotReady, (await rig.Session.GetProductsAsync()).Failure().Code);
    }

    [TestMethod]
    public async Task Requests_BeforeStart_AreNotReady()
    {
        await using Rig rig = Rig.Create();

        Assert.AreEqual(CapitalErrorCodes.HostNotReady, (await rig.Session.SubscribeAsync([UsdJpy])).Failure().Code);
    }

    [TestMethod]
    public async Task InvalidCredentials_StartNothing()
    {
        await using Rig rig = Rig.Create();

        Result result = await rig.Session.StartAsync(new CapitalCredentials("123", "pw"));

        Assert.AreEqual(CapitalErrorCodes.InvalidCredentials, result.Failure().Code);
        Assert.AreEqual(string.Empty, rig.CallLog(), "no process was started");
        Assert.AreEqual(CapitalHostState.Stopped, rig.Session.State);
    }

    [TestMethod]
    public async Task AMissingHostExecutable_IsHostNotFound()
    {
        CapitalHostOptions options = new() { HostExecutablePath = Path.Combine(Path.GetTempPath(), "no-such-capital-host.exe") };
        await using CapitalHostSession session = new(options);

        Result result = await session.StartAsync(Rig.Credentials());

        Assert.AreEqual(CapitalErrorCodes.HostNotFound, result.Failure().Code);
        Assert.AreEqual(CapitalHostState.Stopped, session.State, "nothing was started");
    }

    [TestMethod]
    public async Task SubscribeRejection_CarriesCapitalsCode_AndIsNotRemembered()
    {
        await using Rig rig = Rig.Create(environment: new() { ["FAKE_CAPITAL_REJECT_SYMBOL"] = "SBAD" });
        await rig.StartAsync();

        Result<IReadOnlyList<CapitalSymbol>> result = await rig.Session.SubscribeAsync([new CapitalSymbol("FX", "SBAD")]);

        Assert.AreEqual(CapitalErrorCodes.SubscribeRejected, result.Failure().Code);
        Assert.IsTrue(result.Failure().TryGetInt64(CapitalErrorDataKeys.Code, out long code));
        Assert.AreEqual(1012L, code);
        Assert.AreEqual(0, rig.Session.Subscriptions.Count);
    }

    /// <summary>
    /// 一個工作階段 + 它的假宿主設定、狀態紀錄、日誌與呼叫紀錄檔。
    /// One session with its fake host settings, recorded states, logger and call log.
    /// </summary>
    private sealed class Rig : IAsyncDisposable
    {
        private readonly ConcurrentQueue<CapitalHostState> _states = new();
        private readonly List<CapitalMarketEvent> _events = [];

        private Rig(CapitalHostOptions options, CapitalHostSession session, CapturingLogger<CapitalHostSession> logger, string callLog)
        {
            Options = options;
            Session = session;
            Logger = logger;
            CallLogPath = callLog;
            Session.StateChanged += (_, e) => _states.Enqueue(e.Current);
        }

        public CapitalHostOptions Options { get; }

        public CapitalHostSession Session { get; }

        public CapturingLogger<CapitalHostSession> Logger { get; }

        public string CallLogPath { get; }

        public IReadOnlyList<CapitalHostState> States => [.. _states];

        public static CapitalCredentials Credentials() => new(FakeHost.LoginId, FakeHost.Password);

        public static Rig Create(Dictionary<string, string>? environment = null)
        {
            string callLog = Path.Combine(Path.GetTempPath(), "capital-calls-" + Guid.NewGuid().ToString("N")[..8] + ".log");
            CapitalHostOptions options = new()
            {
                HostExecutablePath = FakeHost.ExecutablePath(),
                StartTimeout = TimeSpan.FromSeconds(15),
                RestartDelays = [TimeSpan.FromMilliseconds(100)],
            };

            options.AdditionalEnvironment["FAKE_CAPITAL_CALL_LOG"] = callLog;
            options.AdditionalEnvironment["FAKE_CAPITAL_KLINE_QUIET_MS"] = "300";
            options.AdditionalEnvironment["FAKE_CAPITAL_KLINE_NODATA_MS"] = "1000";
            options.AdditionalEnvironment["FAKE_CAPITAL_LIVE_TICK_MS"] = "150";

            foreach ((string key, string value) in environment ?? [])
            {
                options.AdditionalEnvironment[key] = value;
            }

            CapturingLogger<CapitalHostSession> logger = new();
            CapitalHostSession session = new(options, logger, new SecretMasker());
            return new Rig(options, session, logger, callLog);
        }

        public async Task StartAsync()
        {
            Result result = await Session.StartAsync(Credentials());
            Assert.IsTrue(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        }

        public string CallLog()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    return File.Exists(CallLogPath) ? File.ReadAllText(CallLogPath) : string.Empty;
                }
                catch (IOException)
                {
                    Thread.Sleep(10);
                }
            }

            return File.ReadAllText(CallLogPath);
        }

        public string[] CallLogLines() => CallLog().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>把佇列裡目前有的事件都收下(累積)。Drains what is queued now, accumulating.</summary>
        public List<CapitalMarketEvent> Drain()
        {
            while (Session.Events.TryRead(out CapitalMarketEvent? item))
            {
                _events.Add(item);
            }

            return _events;
        }

        public async Task<List<CapitalMarketEvent>> ReadUntilAsync(Func<IReadOnlyList<CapitalMarketEvent>, bool> done)
        {
            Assert.IsTrue(await FakeHost.WaitUntilAsync(() => done(Drain()), Wait), "the expected events did not arrive");
            return [.. _events];
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();

            try
            {
                File.Delete(CallLogPath);
            }
            catch (IOException)
            {
                // 假宿主可能還握著檔案。The fake may still hold the file.
            }
        }
    }
}
