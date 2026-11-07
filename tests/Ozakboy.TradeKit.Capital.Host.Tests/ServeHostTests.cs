using System.Text.Json;
using Ozakboy.TradeKit.Capital.Host.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Host.Tests;

/// <summary>
/// 以假 <see cref="ISkQuoteSource"/> 驅動協定層 <see cref="ServeHost"/>(DESIGN §2)。
/// Drives the protocol layer with a fake <see cref="ISkQuoteSource"/>.
/// </summary>
[TestClass]
public sealed class ServeHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task MissingCredentials_ExitTwo_WithoutOpeningSkComOrLoggingIn()
    {
        using HostRig rig =HostRig.Create().Start(ServeLogin.Create(string.Empty, HostRig.Password));

        Assert.AreEqual(ServeExitCodes.MissingCredentials, await rig.ExitCodeAsync());

        JsonElement status = rig.Output.Messages.Single();
        Assert.AreEqual("faulted", status.GetProperty("data").GetProperty("state").GetString());
        Assert.AreEqual("missing_credentials", status.GetProperty("data").GetProperty("reason").GetString());
        Assert.AreEqual(0, rig.Source.Calls.Length, "neither Open nor Login is called");
    }

    [TestMethod]
    public async Task Start_ReportsLoggingInConnectingReady_InOrder()
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();

        // 0.2.0 起國內狀態改變會再送 state 不變的 status;這裡只看海外自己的轉換。
        // Since 0.2.0 domestic changes send further status events with the same state; only overseas transitions count.
        Assert.AreEqual("logging_in|connecting|ready", Join(rig.OverseasStates()));
        Assert.AreEqual("open|login|enter", Join(rig.Source.Calls));
    }

    [TestMethod]
    public async Task LogDirectory_IsSetBeforeLogin()
    {
        using HostRig rig =HostRig.Create().Start(ServeLogin.Create(HostRig.LoginId, HostRig.Password, @"C:\logs\capital"));
        _ = await rig.Output.WaitForAsync(HostRig.IsStatus("ready"), Wait);

        Assert.AreEqual(@"open|set_log_path C:\logs\capital|login|enter", Join(rig.Source.Calls));
    }

    [TestMethod]
    [DataRow(unchecked((int)0x80040154), "skcom_not_registered")]
    [DataRow(unchecked((int)0x8007007E), "skcom_file_missing")]
    public async Task OpenFailure_IsFaultedWithReasonAndHResult_ExitThree(int hresult, string reason)
    {
        using HostRig rig =HostRig.Create();
        rig.Source.OpenFailure = new SkOpenFailure(reason, hresult);
        _ = rig.Start();

        Assert.AreEqual(ServeExitCodes.SkComUnavailable, await rig.ExitCodeAsync());

        JsonElement data = rig.Output.Messages.Single().GetProperty("data");
        Assert.AreEqual(reason, data.GetProperty("reason").GetString());
        Assert.AreEqual($"0x{hresult:X8}", data.GetProperty("hresult").GetString());
        Assert.AreEqual(0, rig.Source.CallCount("login"));
    }

    [TestMethod]
    [DataRow(300)]
    [DataRow(307)]
    public async Task LoginFailure_IsReportedWithItsCode_NeverRetried_ExitFour(int code)
    {
        using HostRig rig =HostRig.Create();
        rig.Source.LoginCode = code;
        _ = rig.Start();

        Assert.AreEqual(ServeExitCodes.LoginFailed, await rig.ExitCodeAsync());

        JsonElement data = rig.Output.Messages.Single(HostRig.IsStatus("login_failed")).GetProperty("data");
        Assert.AreEqual(code, data.GetProperty("code").GetInt32());
        Assert.AreEqual(1, rig.Source.CallCount("login"), "Login is never retried");
        Assert.AreEqual(0, rig.Source.CallCount("enter"));
    }

    [TestMethod]
    public async Task No3001_IsAConnectTimeout_ExitFive()
    {
        using HostRig rig =HostRig.Create();
        rig.Source.AutoConnect = false;
        _ = rig.Start();

        Assert.AreEqual(ServeExitCodes.ConnectFailed, await rig.ExitCodeAsync());
        Assert.AreEqual("connect_timeout", rig.Output.Messages.Single(HostRig.IsStatus("faulted")).GetProperty("data").GetProperty("reason").GetString());
    }

    [TestMethod]
    public async Task EnterMonitorRejected_IsFaulted_ExitFive()
    {
        using HostRig rig =HostRig.Create();
        rig.Source.EnterCode = 1001;
        _ = rig.Start();

        Assert.AreEqual(ServeExitCodes.ConnectFailed, await rig.ExitCodeAsync());
        JsonElement data = rig.Output.Messages.Single(HostRig.IsStatus("faulted")).GetProperty("data");
        Assert.AreEqual("connect_rejected", data.GetProperty("reason").GetString());
        Assert.AreEqual(1001, data.GetProperty("code").GetInt32());
    }

    [TestMethod]
    public async Task RequestsBeforeReady_AreHandledAfterReady()
    {
        using HostRig rig =HostRig.Create().Start();
        rig.Send("""{"id":1,"method":"status"}""");

        JsonElement response = await rig.Output.WaitForAsync(m => m.TryGetProperty("id", out _), Wait);

        int readyIndex = rig.Output.Lines.ToList().FindIndex(line => line.Contains("\"ready\"", StringComparison.Ordinal));
        int responseIndex = rig.Output.Lines.ToList().FindIndex(line => line.StartsWith("{\"id\":1", StringComparison.Ordinal));
        Assert.IsTrue(readyIndex >= 0 && readyIndex < responseIndex, "answered after ready");
        Assert.IsTrue(response.GetProperty("result").GetProperty("connected").GetBoolean());
    }

    // 依據:products.txt 以 ##,,,, 結尾、product-detail.txt 以 ##,,,,,, 結尾;明細多了下單交易所、下單代碼、商品種類。
    // Source: the measured list and details, each ending with a ## row.
    [TestMethod]
    public async Task Products_CollectsUntilTheHashRow_AndMergesTheDetails()
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();

        JsonElement items = (await rig.RequestAsync(1, "products")).GetProperty("result").GetProperty("items");

        Assert.AreEqual(2, items.GetArrayLength());
        JsonElement twd = items[0];
        Assert.AreEqual("FX", twd.GetProperty("exchange").GetString());
        Assert.AreEqual("SUSDTWD", twd.GetProperty("code").GetString());
        Assert.AreEqual("新台幣", twd.GetProperty("name").GetString());
        Assert.AreEqual("99991231", twd.GetProperty("lastTradeDate").GetString());
        Assert.AreEqual("0", twd.GetProperty("firstNoticeDate").GetString());
        Assert.AreEqual(2, twd.GetProperty("kind").GetInt32());
        Assert.AreEqual("--", twd.GetProperty("orderExchange").GetString());

        JsonElement nq = items[1];
        Assert.AreEqual(5, nq.GetProperty("kind").GetInt32());
        Assert.AreEqual("NQ_202612", nq.GetProperty("orderCode").GetString());
        CollectionAssert.Contains(rig.Source.Calls, "products");
        CollectionAssert.Contains(rig.Source.Calls, "detail 1");
    }

    [TestMethod]
    public async Task Products_WithoutTheHashRow_TimeOut()
    {
        using HostRig rig =HostRig.Create();
        rig.Source.ProductRows = ["FX,國際外匯,SUSDTWD,新台幣,99991231,0"];
        await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "products")).GetProperty("error");

        Assert.AreEqual("capital.products.timeout", error.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task Subscribe_UnionsTheSet_JoinsWithHash_AndRequestsTicksPerNewSymbol()
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();

        JsonElement first = await rig.RequestAsync(1, "subscribe", """{"symbols":["FX,SUSDTWD","CME,NQ0000","FX,SUSDTWD"]}""");
        JsonElement second = await rig.RequestAsync(2, "subscribe", """{"symbols":["CME,NQ0000","CBOT,YM0000"]}""");

        Assert.AreEqual("FX,SUSDTWD|CME,NQ0000", Join(Strings(first.GetProperty("result").GetProperty("subscribed"))));
        Assert.AreEqual("FX,SUSDTWD|CME,NQ0000|CBOT,YM0000", Join(Strings(second.GetProperty("result").GetProperty("subscribed"))));
        Assert.AreEqual(
            "stocks FX,SUSDTWD#CME,NQ0000|ticks FX,SUSDTWD|ticks CME,NQ0000|stocks CBOT,YM0000|ticks CBOT,YM0000",
            Join(rig.Source.Calls.Where(call => call.StartsWith("stocks", StringComparison.Ordinal) || call.StartsWith("ticks", StringComparison.Ordinal))));
    }

    [TestMethod]
    public async Task Subscribe_Rejected_CarriesTheCodeAndIsNotRemembered()
    {
        using HostRig rig =HostRig.Create();
        rig.Source.StocksCode = 1012;
        await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "subscribe", """{"symbols":["FX,SBAD"]}""")).GetProperty("error");

        Assert.AreEqual("capital.subscribe.rejected", error.GetProperty("code").GetString());
        Assert.AreEqual(1012, error.GetProperty("data").GetProperty("skCode").GetInt32());
        Assert.AreEqual("FX,SBAD", error.GetProperty("data").GetProperty("symbol").GetString());

        rig.Source.StocksCode = 0;
        JsonElement next = await rig.RequestAsync(2, "subscribe", """{"symbols":["FX,SUSDTWD"]}""");
        Assert.AreEqual("FX,SUSDTWD", Join(Strings(next.GetProperty("result").GetProperty("subscribed"))));
    }

    [TestMethod]
    [DataRow("""{"symbols":["FXSUSDTWD"]}""")]
    [DataRow("""{"symbols":["FX,A#CME,B"]}""")]
    [DataRow("""{"symbols":"FX,SUSDTWD"}""")]
    [DataRow("""{}""")]
    public async Task Subscribe_MalformedSymbols_AreInvalidRequests(string parameters)
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "subscribe", parameters)).GetProperty("error");

        Assert.AreEqual("capital.request.invalid", error.GetProperty("code").GetString());
        Assert.AreEqual(0, rig.Source.CallCount("stocks"));
    }

    // 依據:實測 K 線沒有結束事件、bstrStockNo 不含交易所;這裡另外混進一筆別的代碼(前一個請求的尾巴)。
    // Source: measured K-lines have no end event and bstrStockNo has no exchange; a stray row for another code is mixed in.
    [TestMethod]
    public async Task KLine_EndsAfterSilence_AndKeepsOnlyTheAskedCode()
    {
        using HostRig rig =HostRig.Create();
        rig.Source.KLineRows.Add(("SEURUSD", "2026/11/02 16:01, 1.100000, 1.100000, 1.100000, 1.100000, 1"));
        rig.Source.KLineRows.Add(("SUSDJPY", "2026/11/02 16:01, 157.889999, 157.940002, 157.889999, 157.940002, 141"));
        rig.Source.KLineRows.Add(("SUSDJPY", "2026/11/02 16:02, 157.919998, 157.940002, 157.910004, 157.919998, 108"));
        await rig.StartReadyAsync();

        JsonElement result = (await rig.RequestAsync(1, "kline", """{"symbol":"FX,SUSDJPY","type":0,"start":"20261102","end":"20261105"}""")).GetProperty("result");

        Assert.AreEqual(
            "2026/11/02 16:01, 157.889999, 157.940002, 157.889999, 157.940002, 141|2026/11/02 16:02, 157.919998, 157.940002, 157.910004, 157.919998, 108",
            Join(Strings(result.GetProperty("lines"))));
        CollectionAssert.Contains(rig.Source.Calls, "kline FX,SUSDJPY 0 20261102 20261105 1");
    }

    [TestMethod]
    public async Task KLine_NoRowsAtAll_EndsEmptyAfterTheNoDataLimit()
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();

        DateTime started = DateTime.UtcNow;
        JsonElement result = (await rig.RequestAsync(1, "kline", """{"symbol":"FX,SUSDJPY","type":1,"start":"20261001","end":"20261105"}""")).GetProperty("result");

        Assert.AreEqual(0, result.GetProperty("lines").GetArrayLength());
        Assert.IsTrue(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(450), "waited for the no-data limit");
    }

    [TestMethod]
    public async Task KLine_Rejected_CarriesCapitalsCode()
    {
        using HostRig rig =HostRig.Create();
        rig.Source.KLineCode = 1042;
        await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "kline", """{"symbol":"FX,SUSDJPY","type":3,"start":"20261001","end":"20261105"}""")).GetProperty("error");

        Assert.AreEqual("capital.kline.rejected", error.GetProperty("code").GetString());
        Assert.AreEqual(1042, error.GetProperty("data").GetProperty("skCode").GetInt32());
    }

    [TestMethod]
    [DataRow("""{"symbol":"FX,SUSDJPY","type":4,"start":"20261001","end":"20261105"}""")]
    [DataRow("""{"symbol":"FX,SUSDJPY","type":0,"start":"2026-10-01","end":"20261105"}""")]
    [DataRow("""{"symbol":"SUSDJPY","type":0,"start":"20261001","end":"20261105"}""")]
    public async Task KLine_MalformedParameters_AreInvalidRequests(string parameters)
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "kline", parameters)).GetProperty("error");

        Assert.AreEqual("capital.request.invalid", error.GetProperty("code").GetString());
        Assert.AreEqual(0, rig.Source.CallCount("kline"));
    }

    [TestMethod]
    public async Task UnknownMethodAndBadJson_AreAnsweredWithErrors()
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();

        JsonElement unknown = await rig.RequestAsync(7, "orders");
        Assert.AreEqual("capital.request.unknown_method", unknown.GetProperty("error").GetProperty("code").GetString());

        rig.Send("not json");
        JsonElement bad = await rig.Output.WaitForAsync(m => m.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Null, Wait);
        Assert.AreEqual("capital.request.invalid", bad.GetProperty("error").GetProperty("code").GetString());

        rig.Send("""{"id":"x","method":"status"}""");
        _ = await rig.Output.WaitForAsync(m => m.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Null && m.ToString().Contains("integer", StringComparison.Ordinal), Wait);
    }

    // 依據:實測成交先於報價到;第一次看到索引就查一次並快取,報價事件每次都查(它要取最新值)。
    // Source: ticks were measured to arrive before quotes; the first sight of an index looks it up once and caches it.
    [TestMethod]
    public async Task ATickBeforeAnyQuote_ResolvesTheIndexOnce_AndCarriesDecimals()
    {
        using HostRig rig =HostRig.Create();
        rig.Source.Quotes[137] = new SkQuoteData { ExchangeNo = "FX", StockNo = "SUSDJPY", StockName = "日元", Decimals = 2, Denominator = 1, TradingDay = 20261106, Close = 15784 };
        await rig.StartReadyAsync();

        rig.Source.RaiseTick(new SkTickData(137, 57842, 20261105, 224357, 15784, 1, IsHistory: true));
        rig.Source.RaiseTick(new SkTickData(137, 57843, 20261105, 2501, 15785, 1, IsHistory: false));

        JsonElement live = (await rig.Output.WaitForAsync(m => HostRig.IsEvent("tick")(m) && m.GetProperty("data").GetProperty("ptr").GetInt32() == 57843, Wait)).GetProperty("data");
        JsonElement replay = rig.Output.Messages.First(m => HostRig.IsEvent("tick")(m)).GetProperty("data");

        Assert.AreEqual("FX,SUSDJPY", replay.GetProperty("symbol").GetString());
        Assert.AreEqual("20261105", replay.GetProperty("date").GetString());
        Assert.AreEqual("224357", replay.GetProperty("time").GetString());
        Assert.AreEqual(15784, replay.GetProperty("price").GetInt64());
        Assert.IsTrue(replay.GetProperty("history").GetBoolean());
        Assert.AreEqual(2, replay.GetProperty("decimals").GetInt32());
        Assert.AreEqual("002501", live.GetProperty("time").GetString(), "time is zero-padded to HHmmss");
        Assert.IsFalse(live.GetProperty("history").GetBoolean());
        Assert.AreEqual(1, rig.Source.IndexLookups(137), "looked up once, then cached");

        rig.Source.RaiseQuote(137);
        JsonElement quote = (await rig.Output.WaitForAsync(HostRig.IsEvent("quote"), Wait)).GetProperty("data");
        Assert.AreEqual(15784, quote.GetProperty("close").GetInt64());
        Assert.AreEqual(20261106, quote.GetProperty("tradingDay").GetInt32());
        Assert.AreEqual(2, rig.Source.IndexLookups(137));
    }

    [TestMethod]
    public async Task ATickForAnUnknownIndex_IsDropped()
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();

        rig.Source.RaiseTick(new SkTickData(999, 1, 20261105, 224357, 1, 1, IsHistory: true));
        await Task.Delay(100);

        Assert.IsFalse(rig.Output.Messages.Any(HostRig.IsEvent("tick")));
    }

    [TestMethod]
    public async Task A3002_ReconnectsWithBackoff_ResendsEverySubscription_ThenReady()
    {
        using HostRig rig =HostRig.Create();
        rig.Source.Quotes[137] = new SkQuoteData { ExchangeNo = "FX", StockNo = "SUSDJPY", Decimals = 2, TradingDay = 20261106 };
        await rig.StartReadyAsync();
        _ = await rig.RequestAsync(1, "subscribe", """{"symbols":["FX,SUSDJPY","CME,NQ0000"]}""");
        rig.Source.RaiseTick(new SkTickData(137, 1, 20261105, 224357, 1, 1, IsHistory: true));
        _ = await rig.Output.WaitForAsync(HostRig.IsEvent("tick"), Wait);

        rig.Source.RaiseConnection(3002);
        _ = await rig.Output.WaitForAsync(HostRig.IsStatus("disconnected"), Wait);

        // 斷線中:products 回 not_connected、subscribe 先記住。While disconnected: products is refused, subscribe is remembered.
        JsonElement products = await rig.RequestAsync(2, "products");
        Assert.AreEqual("capital.not_connected", products.GetProperty("error").GetProperty("code").GetString());
        JsonElement remembered = await rig.RequestAsync(3, "subscribe", """{"symbols":["CBOT,YM0000"]}""");
        Assert.AreEqual(3, remembered.GetProperty("result").GetProperty("subscribed").GetArrayLength());

        _ = await rig.Output.WaitForAsync(m => HostRig.IsStatus("ready")(m) && rig.Source.CallCount("enter") == 2, Wait);
        await Task.Delay(50);

        Assert.AreEqual(1, rig.Source.CallCount("login"), "a reconnect never logs in again");
        Assert.AreEqual(2, rig.OverseasStates().Count(state => state == "ready"));
        string[] calls = rig.Source.Calls;
        Assert.AreEqual("stocks FX,SUSDJPY#CME,NQ0000#CBOT,YM0000", calls.Last(call => call.StartsWith("stocks", StringComparison.Ordinal)));
        Assert.AreEqual(2, calls.Count(call => call == "ticks CME,NQ0000"));
        Assert.AreEqual(1, calls.Count(call => call == "ticks CBOT,YM0000"));

        // 重連後索引快取已清,同一個索引要重新查。The index cache was cleared, so the same index is looked up again.
        rig.Source.RaiseTick(new SkTickData(137, 2, 20261105, 224358, 1, 1, IsHistory: false));
        _ = await rig.Output.WaitForAsync(m => HostRig.IsEvent("tick")(m) && m.GetProperty("data").GetProperty("ptr").GetInt32() == 2, Wait);
        Assert.AreEqual(2, rig.Source.IndexLookups(137));
    }

    [TestMethod]
    public async Task AReconnectWithoutAnswer_TimesOutAndTriesAgain()
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();
        rig.Source.AutoConnect = false;

        rig.Source.RaiseConnection(3002);
        Assert.IsTrue(SpinWait.SpinUntil(() => rig.Source.CallCount("enter") >= 3, TimeSpan.FromSeconds(5)), "kept trying after a silent attempt");

        rig.Source.RaiseConnection(3001);
        _ = await rig.Output.WaitForAsync(m => rig.OverseasStates().Count(state => state == "ready") == 2, Wait);
    }

    [TestMethod]
    public async Task Shutdown_LeavesTheMonitorAndExitsZero()
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();

        JsonElement response = await rig.RequestAsync(9, "shutdown");

        Assert.AreEqual(JsonValueKind.Object, response.GetProperty("result").ValueKind);
        Assert.AreEqual(ServeExitCodes.Success, await rig.ExitCodeAsync());
        Assert.AreEqual("leave", rig.Source.Calls[^1]);
    }

    [TestMethod]
    public async Task EndOfInput_LeavesTheMonitorAndExitsZero()
    {
        using HostRig rig =await HostRig.Create().StartReadyAsync();

        rig.Host.CompleteInput();

        Assert.AreEqual(ServeExitCodes.Success, await rig.ExitCodeAsync());
        Assert.AreEqual("leave", rig.Source.Calls[^1]);
    }

    [TestMethod]
    public async Task Stderr_NeverCarriesTheLoginIdOrPassword()
    {
        using HostRig rig =HostRig.Create();
        rig.Source.LoginCode = 300;
        rig.Source.LoginAnnouncement = $"客戶 {HostRig.LoginId} 您好,密碼 {HostRig.Password}";
        rig.Source.LastLogInfo = $"[{HostRig.LoginId.ToLowerInvariant()}] login failed";
        _ = rig.Start(ServeLogin.Create("a123456789", HostRig.Password));
        _ = await rig.ExitCodeAsync();

        string all = rig.Stderr + string.Join('\n', rig.Output.Lines);

        Assert.IsFalse(all.Contains(HostRig.LoginId, StringComparison.OrdinalIgnoreCase), all);
        Assert.IsFalse(all.Contains(HostRig.Password, StringComparison.Ordinal), all);
        StringAssert.Contains(rig.Stderr, "<ID>");
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(element => element.GetString()!)];

    private static string Join(IEnumerable<string> values) => string.Join('|', values);
}
