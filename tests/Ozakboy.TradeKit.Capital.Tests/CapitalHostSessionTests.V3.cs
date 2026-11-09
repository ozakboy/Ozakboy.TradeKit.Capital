using Ozakboy.TradeKit.Capital.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Tests;

// 0.3.0 端對端(對假宿主跑):SKCOM 版本、國內即時報價、海外十檔、海外選擇權、期貨帳務查詢。假宿主的行為依手冊 V2.13.59,
// 2026-11-09 probe-full 實測過的部分照實測(見 tests/FakeCapitalHostExe/FakeSkQuoteSource.*.cs 的註解)。
// 0.3.0 end-to-end against the fake host, which follows the manual and copies what the 2026-11-09 probe measured.
public sealed partial class CapitalHostSessionTests
{
    private const string DomesticAccount = "F0200001234567";
    private const string OverseasAccount = "F0200007654321";

    // 假戶名「王小明」經群益亂碼後的樣子(與假宿主 FakeSkQuoteSource.GarbledHolderName 相同)。The fake holder name as Capital garbles it.
    private const string GarbledHolderName = "?y?pcu";

    private static readonly Dictionary<string, string> FastAccounts = new()
    {
        ["FAKE_CAPITAL_ACCOUNT_QUIET_MS"] = "200",
        ["FAKE_CAPITAL_ACCOUNT_STATUS_QUIET_MS"] = "50",
        ["FAKE_CAPITAL_REPORT_INTERVAL_MS"] = "300",
    };

    [TestMethod]
    public async Task TheSkcomVersionIsReportedAndRecognisedAsTested()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        Assert.AreEqual("2.13.59_x64", rig.Session.SkcomVersion);
        Assert.IsTrue(rig.Session.IsTestedSkcomVersion);
        CapitalConnectionStatus status = (await rig.Session.GetConnectionStatusAsync()).GetValueOrThrow();
        Assert.AreEqual("2.13.59_x64", status.SkcomVersion);
        Assert.IsFalse(rig.Logger.AllText.Contains(CapitalErrorCodes.SkcomVersionUntested, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AnUntestedSkcomVersionIsLoggedOnceAndEverythingKeepsRunning()
    {
        await using Rig rig = Rig.Create(environment: new() { ["FAKE_CAPITAL_SKCOM_VERSION"] = "2.14.1_x64" });
        await rig.StartAsync();

        Assert.AreEqual("2.14.1_x64", rig.Session.SkcomVersion);
        Assert.IsFalse(rig.Session.IsTestedSkcomVersion);
        Assert.AreEqual(CapitalHostState.Ready, rig.Session.State);
        Assert.AreEqual(1, rig.Logger.Entries.Count(entry => entry.Message.Contains(CapitalErrorCodes.SkcomVersionUntested, StringComparison.Ordinal)), "one warning per version");
    }

    [TestMethod]
    public async Task DomesticProductsAreListedByMarket()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        IReadOnlyList<CapitalDomesticProduct> futures = (await rig.Session.GetDomesticProductsAsync(CapitalDomesticMarket.Futures)).GetValueOrThrow();
        IReadOnlyList<CapitalDomesticProduct> options = (await rig.Session.GetDomesticProductsAsync(CapitalDomesticMarket.Options)).GetValueOrThrow();

        Assert.AreEqual("TX00AM,TX00,MTX00AM,MTX00,TM0000AM,TM0000,TXZZ", string.Join(',', futures.Select(product => product.Code)), "the other market's chunk and the end chunk are dropped");
        Assert.AreEqual(new DateOnly(2026, 11, 18), futures[0].LastTradeDate);
        Assert.AreEqual("TXFK600", futures[1].ExchangeCode);
        Assert.AreEqual("100000| 1 |", futures[1].TickSize);
        Assert.IsNull(futures[6].LastTradeDate);
        Assert.AreEqual("TXO39800K6AM,TXO39800K6,TXO11", string.Join(',', options.Select(product => product.Code)));
        Assert.IsNull(options[2].Currency, "an option series row has no currency");
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetDomesticProductsAsync((CapitalDomesticMarket)9)).Failure().Code);
    }

    [TestMethod]
    public async Task ADomesticSubscriptionDeliversQuotesTicksBooksAndLiveBars()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        IReadOnlyList<string> subscribed = (await rig.Session.SubscribeDomesticAsync(["TX00", "TX00"])).GetValueOrThrow();
        Assert.AreEqual("TX00", subscribed.Single());
        Assert.AreEqual("TX00", rig.Session.DomesticSubscriptions.Single());

        List<CapitalMarketEvent> events = await rig.ReadUntilAsync(list =>
            list.OfType<CapitalDomesticQuote>().Count() >= 2
            && list.OfType<CapitalDomesticTick>().Count(tick => tick.IsReplay) >= 3
            && list.OfType<CapitalBest5>().Any(book => book.Market == CapitalQuoteMarket.Domestic)
            && list.OfType<CapitalDomesticLiveBar>().Count() >= 4);

        CapitalDomesticQuote[] quotes = [.. events.OfType<CapitalDomesticQuote>()];
        Assert.IsNull(quotes[0].Close, "the first snapshot after subscribing is partial (measured); zero is no price");
        Assert.IsNotNull(quotes[0].Open);
        Assert.AreEqual(39786.00m, quotes[1].Close);
        Assert.AreEqual(CapitalDomesticMarket.Futures, quotes[1].Market);

        CapitalDomesticTick[] replay = [.. events.OfType<CapitalDomesticTick>().Where(tick => tick.IsReplay)];
        Assert.IsTrue(replay[0].IsSimulated, "the pre-open match is simulated");
        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 0, 45, 0, 123, TimeSpan.Zero), replay[1].TimeUtc);

        CapitalBest5 book = events.OfType<CapitalBest5>().First(item => item.Market == CapitalQuoteMarket.Domestic);
        Assert.AreEqual(39785.99m, book.Bids[0].Price);
        Assert.IsNotNull(book.ExtendedAsk);

        CapitalDomesticLiveBar[] bars = [.. events.OfType<CapitalDomesticLiveBar>()];
        Assert.AreEqual(CapitalLiveBarKind.Reset, bars[0].Kind);
        Assert.IsNull(bars[0].Open);
        Assert.AreEqual(39786.00m, bars[1].Open);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 0, 45, 0, TimeSpan.Zero), bars[1].OpenTimeUtc, "the 0846 bar opened at 08:45 Taipei");
        StringAssert.Contains(rig.CallLog(), "tw_ticks 0 TX00");
    }

    [TestMethod]
    public async Task DomesticSubscriptionArgumentsAndLimitsAreChecked()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.SubscribeDomesticAsync([])).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.SubscribeDomesticAsync(["TX 00"])).Failure().Code);

        Error unknown = (await rig.Session.SubscribeDomesticAsync(["ZZZ"])).Failure();
        Assert.AreEqual(CapitalErrorCodes.SubscribeRejected, unknown.Code);
        Assert.IsTrue(unknown.TryGetInt64(CapitalErrorDataKeys.Code, out long code));
        Assert.AreEqual(3023L, code);

        Error limit = (await rig.Session.SubscribeDomesticAsync([.. Enumerable.Range(1, 11).Select(i => "C" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))])).Failure();
        Assert.AreEqual(CapitalErrorCodes.SubscribeLimitExceeded, limit.Code);
        Assert.IsTrue(limit.TryGetInt64(CapitalErrorDataKeys.Limit, out long max));
        Assert.AreEqual(10L, max);
        Assert.IsEmpty(rig.Session.DomesticSubscriptions);
    }

    [TestMethod]
    public async Task ADomesticDisconnectIsReconnectedAndTheSetIsResent()
    {
        await using Rig rig = Rig.Create(environment: new()
        {
            ["FAKE_CAPITAL_TW_DISCONNECT_AFTER_MS"] = "400",
            ["FAKE_CAPITAL_RECONNECT_DELAYS_MS"] = "50",
        });
        await rig.StartAsync();
        _ = (await rig.Session.SubscribeDomesticAsync(["TX00", "TXO39800K6"])).GetValueOrThrow();

        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.CallLogLines().Count(line => line.EndsWith(" tw_stocks TX00,TXO39800K6", StringComparison.Ordinal)) >= 2, Wait));
        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.Session.DomesticState == CapitalDomesticState.Ready, Wait));
        Assert.IsTrue(rig.DomesticChanges.Any(change => change.Current == CapitalDomesticState.Disconnected));
        Assert.AreEqual(2, rig.CallLogLines().Count(line => line.EndsWith(" tw_ticks 1 TXO39800K6", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task OverseasBooksArriveForTicksAndDepth()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        _ = (await rig.Session.SubscribeAsync([UsdJpy])).GetValueOrThrow();
        IReadOnlyList<CapitalSymbol> depth = (await rig.Session.SubscribeDepthAsync([Nq, Nq])).GetValueOrThrow();
        CollectionAssert.AreEqual(new[] { Nq }, depth.ToArray());

        List<CapitalMarketEvent> events = await rig.ReadUntilAsync(list =>
            list.OfType<CapitalBest5>().Any(book => book.Symbol == UsdJpy) && list.OfType<CapitalBest10>().Count(book => book.Symbol == Nq) >= 2);

        CapitalBest5 best5 = events.OfType<CapitalBest5>().First(book => book.Symbol == UsdJpy);
        Assert.AreEqual(CapitalQuoteMarket.Overseas, best5.Market);
        Assert.AreEqual(157.84m, best5.Bids[1].Price, "two decimals for JPY");
        Assert.IsNull(best5.ExtendedBid);

        CapitalBest10 best10 = events.OfType<CapitalBest10>().First(book => book.Symbol == Nq);
        Assert.HasCount(10, best10.Bids);
        Assert.AreEqual(26983.74m, best10.Bids[0].Price);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.SubscribeDepthAsync([])).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.SubscribeDepthAsync([default])).Failure().Code);
    }

    [TestMethod]
    public async Task OverseasOptionsHaveTheirOwnStateProductsAndEvents()
    {
        await using Rig rig = Rig.Create();
        List<CapitalOverseasOptionsStateChangedEventArgs> changes = [];
        rig.Session.OverseasOptionsStateChanged += (_, e) => changes.Add(e);
        await rig.StartAsync();

        IReadOnlyList<CapitalOverseasOptionProduct> products = (await rig.Session.GetOverseasOptionProductsAsync()).GetValueOrThrow();
        Assert.HasCount(2, products, "the ##,##,##,##,## end row is not a product");
        CapitalSymbol call = products[0].Symbol!.Value;
        Assert.AreEqual(new DateOnly(2026, 12, 18), products[0].LastTradeDate);
        Assert.AreEqual(CapitalOverseasOptionsState.Ready, rig.Session.OverseasOptionsState);
        Assert.AreEqual(CapitalOverseasOptionsState.Connecting, changes[0].Current);

        _ = (await rig.Session.SubscribeOverseasOptionsAsync([call])).GetValueOrThrow();
        _ = (await rig.Session.SubscribeOverseasOptionDepthAsync([call])).GetValueOrThrow();

        List<CapitalMarketEvent> events = await rig.ReadUntilAsync(list =>
            list.OfType<CapitalOverseasOptionQuote>().Any() && list.OfType<CapitalOverseasOptionTick>().Count() >= 2
            && list.OfType<CapitalBest5>().Any(book => book.Market == CapitalQuoteMarket.OverseasOptions)
            && list.OfType<CapitalBest10>().Any(book => book.Market == CapitalQuoteMarket.OverseasOptions));

        CapitalOverseasOptionQuote quote = events.OfType<CapitalOverseasOptionQuote>().First();
        Assert.AreEqual(call, quote.Symbol);
        Assert.AreEqual(1251.00m, quote.Close);
        CapitalOverseasOptionTick tick = events.OfType<CapitalOverseasOptionTick>().First(item => item.IsReplay);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 3, 43, 56, TimeSpan.Zero), tick.TimeUtc);

        CapitalConnectionStatus status = (await rig.Session.GetConnectionStatusAsync()).GetValueOrThrow();
        Assert.AreEqual(CapitalOverseasOptionsState.Ready, status.OverseasOptions);

        _ = await rig.Session.StopAsync();
        Assert.AreEqual(CapitalOverseasOptionsState.NotStarted, rig.Session.OverseasOptionsState);
    }

    [TestMethod]
    public async Task UnavailableOverseasOptionsLeaveEverythingElseRunning()
    {
        await using Rig rig = Rig.Create(environment: new() { ["FAKE_CAPITAL_OO_ENTER_CODE"] = "2026" });
        await rig.StartAsync();

        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.Session.OverseasOptionsState == CapitalOverseasOptionsState.Unavailable, Wait));
        Assert.AreEqual(CapitalDomesticReasons.EnterRejected, rig.Session.OverseasOptionsReason);

        Error error = (await rig.Session.GetOverseasOptionProductsAsync()).Failure();
        Assert.AreEqual(CapitalErrorCodes.OverseasOptionsUnavailable, error.Code);
        Assert.AreEqual(ErrorCategory.Unavailable, error.Category);
        Assert.IsTrue(error.TryGetData(CapitalErrorDataKeys.OverseasOptionsState, out string? state));
        Assert.AreEqual("unavailable", state);

        Assert.AreEqual(CapitalHostState.Ready, rig.Session.State);
        Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.Session.DomesticState == CapitalDomesticState.Ready, Wait));
        _ = (await rig.Session.SubscribeAsync([UsdJpy])).GetValueOrThrow();
    }

    [TestMethod]
    public async Task AccountQueriesInitialiseLazilyAndParseEveryReply()
    {
        await using Rig rig = Rig.Create(environment: FastAccounts);
        await rig.StartAsync();
        Assert.IsFalse(rig.CallLog().Contains("order_init", StringComparison.Ordinal), "nothing is initialised at start-up");

        IReadOnlyList<CapitalAccount> accounts = (await rig.Session.GetAccountsAsync()).GetValueOrThrow();
        Assert.HasCount(3, accounts);
        Assert.AreEqual(DomesticAccount, accounts[0].FullAccount);
        Assert.AreEqual(CapitalAccountKind.OverseasFutures, accounts[1].Kind);

        IReadOnlyList<CapitalFutureRights> rights = (await rig.Session.GetFutureRightsAsync(DomesticAccount)).GetValueOrThrow();
        Assert.HasCount(2, rights, "all currencies: the base currency first");
        Assert.AreEqual("NTD(基幣)", rights[0].Currency, "the garbled base-currency label is repaired");
        Assert.AreEqual("NTD", rights[1].Currency);
        Assert.AreEqual(6000.25m, rights[0].Equity);

        CapitalOpenInterest position = (await rig.Session.GetOpenInterestAsync(DomesticAccount)).GetValueOrThrow().Single();
        Assert.AreEqual(39786.50m, position.AverageCost);
        Assert.IsEmpty((await rig.Session.GetOpenInterestAsync(OverseasAccount)).GetValueOrThrow(), "001 no data");

        CapitalOverseaFutureRights overseaRights = (await rig.Session.GetOverseaFutureRightsAsync(OverseasAccount)).GetValueOrThrow().Single();
        Assert.AreEqual("NTD(基幣)", overseaRights.Currency);
        Assert.AreEqual("000", overseaRights.IbCode);
        Assert.AreEqual(1m, overseaRights.ExchangeRate);

        Assert.IsEmpty((await rig.Session.GetOverseaOpenInterestAsync(DomesticAccount)).GetValueOrThrow(), "@@,001 no data (measured)");
        CapitalOverseaOpenInterest summary = (await rig.Session.GetOverseaOpenInterestAsync(OverseasAccount)).GetValueOrThrow().Single();
        Assert.IsNull(summary.PreviousSettlement, "379 02/4 is a fraction");
        CapitalOverseaOpenInterest detail = (await rig.Session.GetOverseaOpenInterestAsync(OverseasAccount, CapitalOverseaOpenInterestFormat.Detail)).GetValueOrThrow().Single();
        Assert.AreEqual("AB12345", detail.BookNo);

        CapitalOrderReport order = (await rig.Session.GetOrderReportAsync(DomesticAccount)).GetValueOrThrow().Single();
        Assert.AreEqual("TXFK6", order.Symbol);
        CapitalFulfillReport fill = (await rig.Session.GetFulfillReportAsync(DomesticAccount)).GetValueOrThrow().Single();
        Assert.AreEqual(39786m, fill.Price);
        Assert.IsEmpty((await rig.Session.GetOrderReportAsync(OverseasAccount, CapitalOrderReportFormat.Filled)).GetValueOrThrow(), "M003 no data");

        string[] lines = rig.CallLogLines();
        Assert.AreEqual(1, lines.Count(line => line.EndsWith(" order_init", StringComparison.Ordinal)), "initialised once");
        long[] reportTicks = [.. lines.Where(line => line.Contains("_report ", StringComparison.Ordinal)).Select(line => long.Parse(line.Split(' ')[^1], System.Globalization.CultureInfo.InvariantCulture))];
        Assert.HasCount(3, reportTicks);
        Assert.IsTrue(reportTicks.Zip(reportTicks.Skip(1)).All(pair => pair.Second - pair.First >= 280), "the host keeps the interval between report queries");
    }

    [TestMethod]
    public async Task AccountArgumentsAreCheckedLocally()
    {
        await using Rig rig = Rig.Create();
        await rig.StartAsync();

        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetFutureRightsAsync("bad account")).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetFutureRightsAsync(DomesticAccount, (CapitalCoinType)7)).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetOpenInterestAsync(DomesticAccount, 4)).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetOverseaOpenInterestAsync(DomesticAccount, (CapitalOverseaOpenInterestFormat)3)).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetOrderReportAsync(DomesticAccount, (CapitalOrderReportFormat)0)).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetFulfillReportAsync(string.Empty)).Failure().Code);
        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, (await rig.Session.GetOverseaFutureRightsAsync(new string('1', 33))).Failure().Code);
        Assert.IsFalse(rig.CallLog().Contains("order_init", StringComparison.Ordinal), "nothing reached the host");
    }

    [TestMethod]
    [DataRow("FAKE_CAPITAL_ORDER_INIT_CODE", CapitalErrorCodes.AccountInitFailed, "initialize")]
    [DataRow("FAKE_CAPITAL_ORDER_CERT_CODE", CapitalErrorCodes.AccountCertFailed, "read_cert")]
    public async Task AFailedAccountInitialisationIsExplicit(string variable, string expected, string step)
    {
        Dictionary<string, string> environment = new(FastAccounts) { [variable] = "1001" };
        await using Rig rig = Rig.Create(environment: environment);
        await rig.StartAsync();

        Error error = (await rig.Session.GetAccountsAsync()).Failure();

        Assert.AreEqual(expected, error.Code);
        Assert.AreEqual(ErrorCategory.Unavailable, error.Category);
        Assert.IsTrue(error.TryGetData(CapitalErrorDataKeys.Step, out string? actual));
        Assert.AreEqual(step, actual);
        Assert.IsTrue(error.TryGetInt64(CapitalErrorDataKeys.Code, out long code));
        Assert.AreEqual(1001L, code);
    }

    [TestMethod]
    public async Task AMissingOrderLibMakesEveryAccountRequestUnavailable()
    {
        await using Rig rig = Rig.Create(environment: new() { ["FAKE_CAPITAL_ORDER_OPEN_HRESULT"] = "0x80040154" });
        await rig.StartAsync();

        Error error = (await rig.Session.GetOrderReportAsync(DomesticAccount)).Failure();

        Assert.AreEqual(CapitalErrorCodes.AccountUnavailable, error.Code);
        Assert.IsTrue(error.TryGetData(CapitalErrorDataKeys.HResult, out string? hresult));
        Assert.AreEqual("0x80040154", hresult);
    }

    [TestMethod]
    public async Task AccountNumbersNamesAndTheLoginIdNeverReachTheLog()
    {
        Dictionary<string, string> environment = new(FastAccounts) { ["FAKE_CAPITAL_RIGHTS_FAIL"] = "1" };
        await using Rig rig = Rig.Create(environment: environment);
        await rig.StartAsync();

        IReadOnlyList<CapitalAccount> accounts = (await rig.Session.GetAccountsAsync()).GetValueOrThrow();
        Error error = (await rig.Session.GetFutureRightsAsync(DomesticAccount)).Failure();

        Assert.AreEqual(CapitalErrorCodes.AccountQueryFailed, error.Code);
        StringAssert.Contains(error.Message, "<ACCT>");
        Assert.IsFalse(error.Message.Contains("1234567", StringComparison.Ordinal), error.Message);
        Assert.IsFalse(accounts[0].ToString().Contains("1234567", StringComparison.Ordinal));

        await rig.Session.StopAsync();
        string log = rig.Logger.AllText;
        Assert.IsFalse(log.Contains("1234567", StringComparison.Ordinal), log);
        Assert.IsFalse(log.Contains("7654321", StringComparison.Ordinal), log);
        Assert.IsFalse(log.Contains(GarbledHolderName, StringComparison.Ordinal), log);
        Assert.IsFalse(log.Contains(FakeHost.LoginId, StringComparison.OrdinalIgnoreCase), log);
    }

    [TestMethod]
    public async Task ARestartResendsTheDomesticDepthAndOptionSubscriptions()
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
            _ = (await rig.Session.SubscribeDomesticAsync(["MTX00"])).GetValueOrThrow();
            _ = (await rig.Session.SubscribeDepthAsync([Nq])).GetValueOrThrow();
            _ = (await rig.Session.SubscribeOverseasOptionsAsync([new CapitalSymbol("CME", "NQ25000L6")])).GetValueOrThrow();
            _ = (await rig.Session.SubscribeAsync([UsdJpy])).GetValueOrThrow();

            Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.States.Count(s => s == CapitalHostState.Ready) >= 2, Wait), "restarted to Ready");
            Assert.IsTrue(await FakeHost.WaitUntilAsync(() => rig.CallLogLines().Count(line => line.EndsWith(" oo_stocks CME,NQ25000L6", StringComparison.Ordinal)) == 2, Wait));

            string[] lines = rig.CallLogLines();
            Assert.AreEqual(2, lines.Count(line => line.EndsWith(" tw_stocks MTX00", StringComparison.Ordinal)));
            Assert.AreEqual(2, lines.Count(line => line.EndsWith(" depth CME,NQ0000", StringComparison.Ordinal)));
        }
        finally
        {
            File.Delete(marker);
        }
    }
}
