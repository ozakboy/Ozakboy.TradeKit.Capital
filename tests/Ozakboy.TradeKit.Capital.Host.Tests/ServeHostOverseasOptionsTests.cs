using System.Text.Json;
using Ozakboy.TradeKit.Capital.Host.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Host.Tests;

/// <summary>
/// 0.3.0 海外選擇權(SKOOQuoteLib)的協定層:自己的狀態機(不影響海外與國內)、oo-products / oo-subscribe / oo-depth 與事件。
/// 手冊沒有 SKOOQuoteLib 的章節,這裡的行為(3001 算連上、清單以 ## 結尾、# 串接)是比照 SKOSQuoteLib 的推定,待實測。
/// The 0.3.0 overseas option protocol layer; its behaviour is assumed from SKOSQuoteLib and still to be measured.
/// </summary>
[TestClass]
public sealed class ServeHostOverseasOptionsTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task OptionsConnectAfterTheOverseasReadyWithTheirOwnState()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();

        JsonElement ready = (await rig.Output.WaitForAsync(IsOptions("ready"), Wait)).GetProperty("data");

        Assert.AreEqual("ready", ready.GetProperty("state").GetString(), "state stays the overseas one");
        Assert.AreEqual("open|enter", string.Join('|', rig.Source.OptionCalls));
        Assert.AreEqual(1, rig.Source.CallCount("enter"), "the overseas futures side is entered once");
    }

    [TestMethod]
    public async Task ARejectedEnterMakesOnlyTheOptionsUnavailable()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.OptionsEnterCode = 2026;
        _ = await rig.StartReadyAsync();

        JsonElement data = (await rig.Output.WaitForAsync(IsOptions("unavailable"), Wait)).GetProperty("data");
        Assert.AreEqual("enter_rejected", data.GetProperty("overseasOptionsReason").GetString());
        Assert.AreEqual(2026, data.GetProperty("overseasOptionsCode").GetInt32());
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);

        JsonElement error = (await rig.RequestAsync(1, "oo-products")).GetProperty("error");
        Assert.AreEqual("capital.overseas_options.unavailable", error.GetProperty("code").GetString());
        Assert.AreEqual("unavailable", error.GetProperty("data").GetProperty("overseasOptions").GetString());
        Assert.AreEqual(2026, error.GetProperty("data").GetProperty("skCode").GetInt32());

        JsonElement subscribe = (await rig.RequestAsync(2, "oo-subscribe", """{"symbols":["CME,NQ2612C25000"]}""")).GetProperty("error");
        Assert.AreEqual("capital.overseas_options.unavailable", subscribe.GetProperty("code").GetString());

        _ = await rig.RequestAsync(3, "shutdown");
        Assert.AreEqual(ServeExitCodes.Success, await rig.ExitCodeAsync());
        Assert.AreEqual(0, rig.Source.OptionCallCount("leave"), "never entered, so never left");
    }

    [TestMethod]
    public async Task AnOpenFailureIsUnavailableFromTheFirstReady()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.OptionsOpenFailure = new SkOpenFailure("skcom_not_registered", unchecked((int)0x80040154));
        _ = await rig.StartReadyAsync();

        JsonElement ready = rig.Output.Messages.First(HostRig.IsStatus("ready")).GetProperty("data");
        Assert.AreEqual("unavailable", ready.GetProperty("overseasOptions").GetString());
        Assert.AreEqual(0, rig.Source.OptionCallCount("enter"));
    }

    [TestMethod]
    public async Task NoConnectEventInTimeIsUnavailableAndNotRetried()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.OptionsAutoReady = false;
        _ = await rig.StartReadyAsync();

        rig.Send("""{"id":1,"method":"oo-products"}""");
        JsonElement data = (await rig.Output.WaitForAsync(IsOptions("unavailable"), Wait)).GetProperty("data");
        JsonElement held = await rig.Output.WaitForAsync(IsId(1), Wait);

        Assert.AreEqual("ready_timeout", data.GetProperty("overseasOptionsReason").GetString());
        Assert.AreEqual("capital.overseas_options.unavailable", held.GetProperty("error").GetProperty("code").GetString(), "the held request is answered");
        await Task.Delay(200);
        Assert.AreEqual(1, rig.Source.OptionCallCount("enter"));

        // 逾時後才到的連線事件也接受。A late connect event is accepted.
        rig.Source.RaiseOptionConnection(3001);
        _ = await rig.Output.WaitForAsync(IsOptions("ready"), Wait);
    }

    [TestMethod]
    public async Task ADisconnectReconnectsAndResendsBothSubscriptions()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(IsOptions("ready"), Wait);
        _ = await rig.RequestAsync(1, "oo-subscribe", """{"symbols":["CME,NQ2612C25000"]}""");
        _ = await rig.RequestAsync(2, "oo-depth", """{"symbols":["CME,NQ2612C25000"]}""");

        rig.Source.RaiseOptionConnection(3002);
        _ = await rig.Output.WaitForAsync(IsOptions("disconnected"), Wait);

        Assert.IsTrue(await ServeHostLiveTests.WaitUntilAsync(() => rig.Source.OptionCallCount("stocks") == 2 && rig.Source.OptionCallCount("depth") == 2));
        Assert.AreEqual(2, rig.Source.OptionCallCount("ticks CME,NQ2612C25000"));
        Assert.AreEqual(2, rig.Source.OptionCallCount("enter"));
        Assert.AreEqual(1, rig.Source.CallCount("enter"), "the overseas futures side is untouched");
    }

    [TestMethod]
    public async Task ProductsAndSubscriptionsWaitForTheFirstConnect()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.OptionsAutoReady = false;
        _ = await rig.StartReadyAsync();
        _ = await rig.Output.WaitForAsync(IsOptions("connecting"), Wait);

        rig.Send("""{"id":1,"method":"oo-subscribe","params":{"symbols":["CME,NQ2612C25000","CME,NQ2612C25000"]}}""");
        rig.Send("""{"id":2,"method":"oo-products"}""");
        await Task.Delay(100);
        Assert.IsFalse(rig.Output.Messages.Any(message => message.TryGetProperty("id", out _)));

        rig.Source.RaiseOptionConnection(3001);
        JsonElement subscribe = (await rig.Output.WaitForAsync(IsId(1), Wait)).GetProperty("result");
        JsonElement products = (await rig.Output.WaitForAsync(IsId(2), Wait)).GetProperty("result");

        CollectionAssert.AreEqual(Expected("CME,NQ2612C25000"), ServeHostLiveTests.Strings(subscribe, "subscribed"));
        CollectionAssert.AreEqual(Expected("CME,芝加哥商品交易所,NQ2612C25000,小那買權,20261218"), ServeHostLiveTests.Strings(products, "items"));
        CollectionAssert.Contains(rig.Source.OptionCalls, "stocks CME,NQ2612C25000");
    }

    [TestMethod]
    public async Task ProductsWithoutTheEndMarkerTimeOut()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.OptionProductRows = ["CME,芝加哥商品交易所,NQ2612C25000,小那買權,20261218"];
        _ = await rig.StartReadyAsync();
        _ = await rig.Output.WaitForAsync(IsOptions("ready"), Wait);

        JsonElement error = (await rig.RequestAsync(1, "oo-products")).GetProperty("error");

        Assert.AreEqual("capital.products.timeout", error.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task OptionEventsCarryTheSymbolAndDecimals()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        rig.Source.OptionQuotes[500] = new SkOptionQuote { ExchangeNo = "CME", StockNo = "NQ2612C25000", Decimals = 2, Close = 125100, StrikePrice = 2500000, CallPut = "C" };

        rig.Source.RaiseOptionQuote(500);
        rig.Source.RaiseOptionTick(new SkTickData(500, 3, 20261105, 214356, 125050, 2, IsHistory: true));
        rig.Source.RaiseOptionTick(new SkTickData(501, 3, 20261105, 214356, 125050, 2, IsHistory: true));
        rig.Source.RaiseOptionBest5(new SkBook(-1, 500, [1, 2, 3, 4, 5], [1, 1, 1, 1, 1], [6, 7, 8, 9, 10], [1, 1, 1, 1, 1]));
        rig.Source.RaiseOptionBest10(new SkBook(-1, 500, [.. Enumerable.Range(1, 10).Select(i => (long)i)], [.. Enumerable.Repeat(1L, 10)], [.. Enumerable.Range(11, 10).Select(i => (long)i)], [.. Enumerable.Repeat(1L, 10)]));

        JsonElement quote = (await rig.Output.WaitForAsync(HostRig.IsEvent("oo-quote"), Wait)).GetProperty("data");
        JsonElement tick = (await rig.Output.WaitForAsync(HostRig.IsEvent("oo-tick"), Wait)).GetProperty("data");
        _ = await rig.Output.WaitForAsync(HostRig.IsEvent("oo-best5"), Wait);
        JsonElement best10 = (await rig.Output.WaitForAsync(HostRig.IsEvent("oo-best10"), Wait)).GetProperty("data");

        Assert.AreEqual("CME,NQ2612C25000", quote.GetProperty("symbol").GetString());
        Assert.AreEqual(2500000, quote.GetProperty("strike").GetInt64());
        Assert.AreEqual("20261105", tick.GetProperty("date").GetString());
        Assert.AreEqual(2, tick.GetProperty("decimals").GetInt32());
        Assert.AreEqual(10, best10.GetProperty("askPrices").GetArrayLength());
        Assert.AreEqual(1, rig.Output.Messages.Count(HostRig.IsEvent("oo-tick")), "an unknown index is dropped");
    }

    [TestMethod]
    public async Task AnInvalidOptionSubscribeIsRejected()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "oo-depth", """{"symbols":[]}""")).GetProperty("error");

        Assert.AreEqual("capital.request.invalid", error.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task EndOfInputLeavesTheOptionMonitorToo()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(IsOptions("ready"), Wait);

        rig.Host.CompleteInput();

        Assert.AreEqual(ServeExitCodes.Success, await rig.ExitCodeAsync());
        Assert.AreEqual(1, rig.Source.OptionCallCount("leave"));
    }

    private static Func<JsonElement, bool> IsOptions(string state) => element =>
        element.TryGetProperty("event", out JsonElement name) && name.GetString() == "status"
        && element.GetProperty("data").TryGetProperty("overseasOptions", out JsonElement value) && value.GetString() == state;

    private static string[] Expected(params string[] values) => values;

    private static Func<JsonElement, bool> IsId(long id) => element =>
        element.TryGetProperty("id", out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.GetInt64() == id;
}
