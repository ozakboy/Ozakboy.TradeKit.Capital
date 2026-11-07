using System.Text.Json;
using Ozakboy.TradeKit.Capital.Host.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Host.Tests;

/// <summary>
/// 國內報價(SKQuoteLib)的協定層:連線狀態機與 <c>kline-tw</c>。行為依據 2026-11-07 <c>probe-tw</c> 實測(SKCOM 2.13.59):
/// EnterMonitorLONG 回 0 → 3001 → 約 0.5 秒後 3003;每個 K 線請求以一則 OnKLineComplete("##") 結束;代碼不存在立即回非 0。
/// The protocol layer of the domestic quote side, by the 2026-11-07 probe-tw measurements.
/// </summary>
[TestClass]
public sealed class ServeHostDomesticTests
{
    private const string KLineParams = """{"code":"TX00","type":0,"session":0,"start":"20261102","end":"20261106"}""";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task DomesticConnectsOnlyAfterOverseasReadyAndBecomesReadyOn3003()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();

        JsonElement ready = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);

        List<string> lines = [.. rig.Output.Lines];
        int overseasReady = lines.FindIndex(line => line.Contains("\"state\":\"ready\"", StringComparison.Ordinal));
        int domesticConnecting = lines.FindIndex(line => line.Contains("\"domestic\":\"connecting\"", StringComparison.Ordinal));

        Assert.IsTrue(overseasReady >= 0 && overseasReady < domesticConnecting, "the domestic side starts after the overseas ready");
        Assert.AreEqual("ready", ready.GetProperty("data").GetProperty("state").GetString(), "state is still the overseas state");
        Assert.IsFalse(ready.GetProperty("data").TryGetProperty("domesticReason", out _));
        Assert.AreEqual("open|enter", string.Join('|', rig.Source.DomesticCalls));
        Assert.AreEqual(1, rig.Source.CallCount("enter"), "the overseas side is not touched");
    }

    [TestMethod]
    public async Task DomesticEnterRejectedIsUnavailableWhileOverseasStaysReady()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticEnterCode = 9999;
        _ = await rig.StartReadyAsync();

        JsonElement data = (await rig.Output.WaitForAsync(HostRig.IsDomestic("unavailable"), Wait)).GetProperty("data");

        Assert.AreEqual("enter_rejected", data.GetProperty("domesticReason").GetString());
        Assert.AreEqual(9999, data.GetProperty("domesticCode").GetInt32());

        JsonElement status = (await rig.RequestAsync(1, "status")).GetProperty("result");
        Assert.IsTrue(status.GetProperty("connected").GetBoolean(), "overseas still connected");
        Assert.AreEqual("unavailable", status.GetProperty("domestic").GetString());
        Assert.AreEqual("enter_rejected", status.GetProperty("domesticReason").GetString());
        Assert.IsFalse(rig.Run!.IsCompleted, "the host keeps running");

        _ = await rig.RequestAsync(2, "shutdown");
        Assert.AreEqual(ServeExitCodes.Success, await rig.ExitCodeAsync(), "the exit code is unaffected");
        Assert.AreEqual(0, rig.Source.DomesticCallCount("leave"), "never entered, so never left");
    }

    [TestMethod]
    public async Task DomesticWithout3003InTimeIsUnavailableAndNotRetried()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticAutoReady = false;
        _ = await rig.StartReadyAsync();

        JsonElement data = (await rig.Output.WaitForAsync(HostRig.IsDomestic("unavailable"), Wait)).GetProperty("data");

        Assert.AreEqual("ready_timeout", data.GetProperty("domesticReason").GetString());
        await Task.Delay(300);
        Assert.AreEqual(1, rig.Source.DomesticCallCount("enter"), "a first-time timeout is not retried");
    }

    [TestMethod]
    public async Task ALate3003AfterTheTimeoutStillMakesDomesticReady()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticAutoReady = false;
        _ = await rig.StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("unavailable"), Wait);

        rig.Source.RaiseDomesticConnection(3003);

        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);
    }

    [TestMethod]
    public async Task DomesticOpenFailureIsReportedOnTheFirstReadyAndNeverEntered()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticOpenFailure = new SkOpenFailure("skcom_not_registered", unchecked((int)0x80040154));
        _ = await rig.StartReadyAsync();

        JsonElement ready = rig.Output.Messages.First(HostRig.IsStatus("ready")).GetProperty("data");

        Assert.AreEqual("unavailable", ready.GetProperty("domestic").GetString());
        Assert.AreEqual("skcom_not_registered", ready.GetProperty("domesticReason").GetString());
        Assert.AreEqual(0, rig.Source.DomesticCallCount("enter"));
    }

    [TestMethod]
    public async Task DomesticOpenThrowingIsUnavailableAndOverseasStillStarts()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticOpenThrows = true;
        _ = await rig.StartReadyAsync();

        JsonElement ready = rig.Output.Messages.First(HostRig.IsStatus("ready")).GetProperty("data");

        Assert.AreEqual("unavailable", ready.GetProperty("domestic").GetString());
        Assert.AreEqual("skcom_create_failed", ready.GetProperty("domesticReason").GetString());
        Assert.AreEqual(1, rig.Source.CallCount("login"));
    }

    // 依據:OnNotifyKLineData 帶代碼、每個請求一則 OnKLineComplete("##")(2026-11-07 連續送請求實測可依代碼分開)。
    [TestMethod]
    public async Task KLineTwEndsOnTheCompleteEventAndKeepsOnlyTheAskedCode()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticKLineRows.Add(("MTX00", "2026/11/06 13:45, 1.00, 1.00, 1.00, 1.00, 1"));
        rig.Source.DomesticKLineRows.Add(("TX00", "2026/10/30 15:01, 39500.00, 39510.00, 39490.00, 39505.00, 120"));
        rig.Source.DomesticKLineRows.Add(("TX00", "2026/10/30 15:02, 39505.00, 39505.00, 39505.00, 39505.00, 0"));
        _ = await rig.StartReadyAsync();

        JsonElement result = (await rig.RequestAsync(1, "kline-tw", KLineParams)).GetProperty("result");

        string[] lines = [.. result.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!)];
        Assert.AreEqual(2, lines.Length, "the MTX00 row is dropped");
        Assert.AreEqual("2026/10/30 15:02, 39505.00, 39505.00, 39505.00, 39505.00, 0", lines[1], "empty bars are passed through");
        CollectionAssert.Contains(rig.Source.DomesticCalls, "kline TX00 0 1 0 20261102 20261106 1", "sOutType 1 and sMinuteNumber 1, as measured");
    }

    [TestMethod]
    public async Task KLineTwWithNoRowsStillEndsOnTheCompleteEvent()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();

        JsonElement result = (await rig.RequestAsync(1, "kline-tw", """{"code":"TM0000","type":4,"session":1,"start":"20261102","end":"20261102"}""")).GetProperty("result");

        Assert.AreEqual(0, result.GetProperty("lines").GetArrayLength());
        CollectionAssert.Contains(rig.Source.DomesticCalls, "kline TM0000 4 1 1 20261102 20261102 1");
    }

    [TestMethod]
    public async Task KLineTwWithoutACompleteEventTimesOut()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticAutoComplete = false;
        rig.Source.DomesticKLineRows.Add(("TX00", "2026/10/30 15:01, 39500.00, 39510.00, 39490.00, 39505.00, 120"));
        _ = await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "kline-tw", KLineParams)).GetProperty("error");

        Assert.AreEqual("capital.kline.timeout", error.GetProperty("code").GetString());
        Assert.AreEqual(1, error.GetProperty("data").GetProperty("received").GetInt32());
        Assert.AreEqual("TX00", error.GetProperty("data").GetProperty("symbol").GetString());
    }

    [TestMethod]
    public async Task TheLateCompleteOfATimedOutRequestDoesNotEndTheNextOneEarly()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticAutoComplete = false;
        _ = await rig.StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);

        Assert.AreEqual("capital.kline.timeout", (await rig.RequestAsync(1, "kline-tw", KLineParams)).GetProperty("error").GetProperty("code").GetString());

        rig.Source.DomesticKLineRows.Add(("TX00", "2026/10/30 15:01, 39500.00, 39510.00, 39490.00, 39505.00, 120"));
        rig.Send("""{"id":2,"method":"kline-tw","params":""" + KLineParams + "}");
        await Task.Delay(100);

        // 第一個請求遲到的結束事件:必須被吃掉。The first request's late completion must be swallowed.
        rig.Source.RaiseDomesticKLineComplete();
        await Task.Delay(100);
        Assert.IsFalse(rig.Output.Messages.Any(IsResponse(2)), "the stale completion did not end request 2");

        rig.Source.RaiseDomesticKLineComplete();
        JsonElement result = (await rig.Output.WaitForAsync(IsResponse(2), Wait)).GetProperty("result");
        Assert.AreEqual(1, result.GetProperty("lines").GetArrayLength());
    }

    [TestMethod]
    public async Task ALateCompleteWithNoRequestInFlightIsSwallowed()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticAutoComplete = false;
        _ = await rig.StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);
        _ = await rig.RequestAsync(1, "kline-tw", KLineParams);

        rig.Source.RaiseDomesticKLineComplete();
        await Task.Delay(50);
        rig.Source.DomesticAutoComplete = true;

        JsonElement response = await rig.RequestAsync(2, "kline-tw", KLineParams);
        Assert.IsTrue(response.TryGetProperty("result", out _), "the next request completes normally");
    }

    [TestMethod]
    [DataRow(9999)]
    [DataRow(3023)]
    public async Task KLineTwRejectedCarriesCapitalsCode(int code)
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticKLineCode = code;
        _ = await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "kline-tw", KLineParams)).GetProperty("error");

        Assert.AreEqual("capital.kline.rejected", error.GetProperty("code").GetString());
        Assert.AreEqual(code, error.GetProperty("data").GetProperty("skCode").GetInt32());
        Assert.AreEqual("TX00", error.GetProperty("data").GetProperty("symbol").GetString());
    }

    [TestMethod]
    public async Task KLineTwWhileDomesticIsUnavailableIsAnExplicitError()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticEnterCode = 9999;
        _ = await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "kline-tw", KLineParams)).GetProperty("error");

        Assert.AreEqual("capital.domestic.unavailable", error.GetProperty("code").GetString());
        JsonElement data = error.GetProperty("data");
        Assert.AreEqual("unavailable", data.GetProperty("domestic").GetString());
        Assert.AreEqual("enter_rejected", data.GetProperty("reason").GetString());
        Assert.AreEqual(9999, data.GetProperty("skCode").GetInt32());
        Assert.AreEqual(0, rig.Source.DomesticCallCount("kline"));
    }

    [TestMethod]
    public async Task KLineTwSentWhileConnectingWaitsForTheOutcome()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticAutoReady = false;
        _ = await rig.StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("connecting"), Wait);

        rig.Send("""{"id":1,"method":"kline-tw","params":""" + KLineParams + "}");
        JsonElement status = (await rig.RequestAsync(2, "status")).GetProperty("result");

        Assert.AreEqual("connecting", status.GetProperty("domestic").GetString());
        Assert.IsFalse(rig.Output.Messages.Any(IsResponse(1)), "kline-tw waits while other requests go on");

        rig.Source.RaiseDomesticConnection(3003);

        JsonElement response = await rig.Output.WaitForAsync(IsResponse(1), Wait);
        Assert.IsTrue(response.TryGetProperty("result", out _));
    }

    [TestMethod]
    public async Task KLineTwSentWhileConnectingFailsWhenTheConnectTimesOut()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticAutoReady = false;
        _ = await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "kline-tw", KLineParams)).GetProperty("error");

        Assert.AreEqual("capital.domestic.unavailable", error.GetProperty("code").GetString());
        Assert.AreEqual("ready_timeout", error.GetProperty("data").GetProperty("reason").GetString());
    }

    [TestMethod]
    [DataRow("""{"code":"TX00","type":1,"session":0,"start":"20261102","end":"20261106"}""")]
    [DataRow("""{"code":"TX00","type":0,"session":2,"start":"20261102","end":"20261106"}""")]
    [DataRow("""{"code":"TX,00","type":0,"session":0,"start":"20261102","end":"20261106"}""")]
    [DataRow("""{"code":"TX 00","type":0,"session":0,"start":"20261102","end":"20261106"}""")]
    [DataRow("""{"code":"","type":0,"session":0,"start":"20261102","end":"20261106"}""")]
    [DataRow("""{"code":"TX00","type":0,"session":0,"start":"2026-11-02","end":"20261106"}""")]
    [DataRow("""{"code":"TX00","type":0,"start":"20261102","end":"20261106"}""")]
    [DataRow("""{"code":"TX00","type":"0","session":0,"start":"20261102","end":"20261106"}""")]
    public async Task KLineTwMalformedParametersAreInvalidRequests(string parameters)
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "kline-tw", parameters)).GetProperty("error");

        Assert.AreEqual("capital.request.invalid", error.GetProperty("code").GetString());
        Assert.AreEqual(0, rig.Source.DomesticCallCount("kline"));
    }

    [TestMethod]
    public async Task ADomestic3002ReconnectsWithBackoffAndLeavesOverseasAlone()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);

        rig.Source.RaiseDomesticConnection(3002);

        JsonElement disconnected = (await rig.Output.WaitForAsync(HostRig.IsDomestic("disconnected"), Wait)).GetProperty("data");
        Assert.AreEqual("connection_lost", disconnected.GetProperty("domesticReason").GetString());
        Assert.AreEqual("ready", disconnected.GetProperty("state").GetString(), "the overseas side is still ready");

        Assert.IsTrue(SpinWait.SpinUntil(() => rig.Source.DomesticCallCount("enter") == 2, Wait), "re-entered after the backoff");
        Assert.IsTrue(
            SpinWait.SpinUntil(() => rig.Output.Lines.Count(line => line.Contains("\"domestic\":\"ready\"", StringComparison.Ordinal)) >= 2, Wait),
            "ready again after the reconnect");

        Assert.AreEqual(1, rig.Source.CallCount("enter"), "the overseas monitor is not re-entered");
        Assert.AreEqual(1, rig.Source.CallCount("login"), "a reconnect never logs in again");
    }

    [TestMethod]
    public async Task ADomesticReconnectWithoutAnswerTriesAgain()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);
        rig.Source.DomesticAutoReady = false;

        rig.Source.RaiseDomesticConnection(3002);

        Assert.IsTrue(SpinWait.SpinUntil(() => rig.Source.DomesticCallCount("enter") >= 3, Wait), "kept trying after a silent attempt");
        StringAssert.Contains(rig.Stderr, "國內重連逾時");
    }

    [TestMethod]
    public async Task ADomesticReconnectRejectedTriesAgain()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);
        rig.Source.DomesticEnterCode = 1;

        rig.Source.RaiseDomesticConnection(3002);

        Assert.IsTrue(SpinWait.SpinUntil(() => rig.Source.DomesticCallCount("enter") >= 3, Wait), "a rejected reconnect backs off and retries");
    }

    [TestMethod]
    public async Task A3002DuringADomesticReconnectBacksOffAgain()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);
        rig.Source.DomesticAutoReady = false;
        rig.Source.RaiseDomesticConnection(3002);
        Assert.IsTrue(SpinWait.SpinUntil(() => rig.Source.DomesticCallCount("enter") == 2, Wait));

        rig.Source.RaiseDomesticConnection(3002);

        Assert.IsTrue(SpinWait.SpinUntil(() => rig.Source.DomesticCallCount("enter") >= 3, Wait));
    }

    [TestMethod]
    public async Task KLineTwWhileDomesticIsDisconnectedIsAnExplicitError()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);
        rig.Source.DomesticAutoReady = false;
        rig.Source.RaiseDomesticConnection(3002);
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("disconnected"), Wait);

        JsonElement data = (await rig.RequestAsync(1, "kline-tw", KLineParams)).GetProperty("error").GetProperty("data");

        Assert.AreEqual("disconnected", data.GetProperty("domestic").GetString());
        Assert.AreEqual("connection_lost", data.GetProperty("reason").GetString());
    }

    [TestMethod]
    public async Task AnOverseas3002CarriesTheDomesticStateAlong()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);

        rig.Source.RaiseConnection(3002);

        JsonElement data = (await rig.Output.WaitForAsync(HostRig.IsStatus("disconnected"), Wait)).GetProperty("data");
        Assert.AreEqual("ready", data.GetProperty("domestic").GetString(), "the domestic side is unaffected");
    }

    [TestMethod]
    public async Task ShutdownLeavesTheDomesticMonitorToo()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);

        _ = await rig.RequestAsync(1, "shutdown");

        Assert.AreEqual(ServeExitCodes.Success, await rig.ExitCodeAsync());
        Assert.AreEqual("leave", rig.Source.DomesticCalls[^1]);
        Assert.AreEqual("leave", rig.Source.Calls[^1]);
    }

    [TestMethod]
    public async Task EndOfInputLeavesTheDomesticMonitorToo()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);

        rig.Host.CompleteInput();

        Assert.AreEqual(ServeExitCodes.Success, await rig.ExitCodeAsync());
        Assert.AreEqual("leave", rig.Source.DomesticCalls[^1]);
    }

    [TestMethod]
    [DataRow("TX00", true)]
    [DataRow("MTX00", true)]
    [DataRow("TM0000", true)]
    [DataRow("", false)]
    [DataRow("TX#00", false)]
    [DataRow("TX\t00", false)]
    [DataRow("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456", false)]
    public void DomesticCodesAreCheckedBeforeAnythingIsSent(string code, bool valid) =>
        Assert.AreEqual(valid, ServeHost.IsValidDomesticCode(code));

    private static Func<JsonElement, bool> IsResponse(long id) => element =>
        element.TryGetProperty("id", out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.GetInt64() == id;
}
