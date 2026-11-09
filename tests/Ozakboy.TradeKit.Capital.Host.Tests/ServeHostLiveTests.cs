using System.Text.Json;
using Ozakboy.TradeKit.Capital.Host.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Host.Tests;

/// <summary>
/// 0.3.0 協定層:SKCOM 版本、國內即時報價(tw-products / tw-subscribe 與事件)、海外十檔(depth)。行為依手冊 V2.13.59 推定(待實測):
/// 國內 RequestStocks 代碼以逗號分隔、頁碼固定 1、一個物件一組訂閱(每次送完整集合);RequestTicks 頁碼從 0 起、一頁一檔、最多 10 檔。
/// The 0.3.0 protocol layer, by the V2.13.59 manual (still to be measured).
/// </summary>
[TestClass]
public sealed class ServeHostLiveTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task TheVersionIsReadBeforeLoginAndCarriedByEveryStatus()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();

        // logging_in 在呼叫 Login 之前寫出,它已經帶版本 → 版本是登入前讀的。logging_in precedes Login, so the version was read first.
        JsonElement loggingIn = rig.Output.Messages.First(HostRig.IsStatus("logging_in")).GetProperty("data");
        Assert.AreEqual("2.13.59_x64", loggingIn.GetProperty("skcomVersion").GetString());
        Assert.IsTrue(rig.Output.Messages.Where(HostRig.IsEvent("status")).All(message => message.GetProperty("data").TryGetProperty("skcomVersion", out _)));
        CollectionAssert.Contains(rig.Source.LiveCalls, "version " + HostRig.LoginId, "the manual's parameter is the login ID");
        Assert.IsTrue(await WaitUntilAsync(() => rig.Output.Lines.Any(line => line.Contains("\"overseasOptions\":\"ready\"", StringComparison.Ordinal))));

        JsonElement status = (await rig.RequestAsync(1, "status")).GetProperty("result");
        Assert.AreEqual("2.13.59_x64", status.GetProperty("skcomVersion").GetString());
        Assert.AreEqual("ready", status.GetProperty("overseasOptions").GetString());
        Assert.IsFalse(rig.Stderr.Contains(HostRig.LoginId, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task AVersionFailureIsOnlyLogged()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.ApiVersionThrows = true;
        _ = await rig.StartReadyAsync();

        JsonElement ready = rig.Output.Messages.First(HostRig.IsStatus("ready")).GetProperty("data");
        Assert.IsFalse(ready.TryGetProperty("skcomVersion", out _));
        StringAssert.Contains(rig.Stderr, "讀 SKCOM 版本失敗");
    }

    [TestMethod]
    public async Task AnEmptyVersionIsOnlyLogged()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.ApiVersion = "  ";
        _ = await rig.StartReadyAsync();

        Assert.IsFalse(rig.Output.Messages.First(HostRig.IsStatus("ready")).GetProperty("data").TryGetProperty("skcomVersion", out _));
    }

    // 依據:手冊 4-4-2(一個物件一組即時報價訂閱、代碼以逗號分隔、頁碼帶 1)、4-4-3(頁碼從 0,一頁一檔)。
    [TestMethod]
    public async Task DomesticSubscribeSendsTheWholeSetAndOnePagePerCode()
    {
        using HostRig rig = await ReadyWithDomesticAsync();

        JsonElement first = (await rig.RequestAsync(1, "tw-subscribe", """{"codes":["TX00"]}""")).GetProperty("result");
        JsonElement second = (await rig.RequestAsync(2, "tw-subscribe", """{"codes":["MTX00","TX00"]}""")).GetProperty("result");

        CollectionAssert.AreEqual(Expected("TX00"), Strings(first, "subscribed"));
        CollectionAssert.AreEqual(Expected("TX00", "MTX00"), Strings(second, "subscribed"));
        string[] calls = rig.Source.LiveCalls;
        CollectionAssert.AreEqual(Expected("tw_stocks TX00", "tw_stocks TX00,MTX00"), calls.Where(call => call.StartsWith("tw_stocks", StringComparison.Ordinal)).ToArray());
        CollectionAssert.AreEqual(Expected("tw_ticks 0 TX00", "tw_ticks 1 MTX00"), calls.Where(call => call.StartsWith("tw_ticks", StringComparison.Ordinal)).ToArray());
        CollectionAssert.Contains(calls, "tw_lookup 2 TX00");
    }

    [TestMethod]
    public async Task AnOptionCodeIsFoundInTheOptionsMarket()
    {
        using HostRig rig = await ReadyWithDomesticAsync();

        _ = (await rig.RequestAsync(1, "tw-subscribe", """{"codes":["TXO39800K6"]}""")).GetProperty("result");

        string[] lookups = [.. rig.Source.LiveCalls.Where(call => call.StartsWith("tw_lookup", StringComparison.Ordinal))];
        CollectionAssert.AreEqual(Expected("tw_lookup 2 TXO39800K6", "tw_lookup 3 TXO39800K6"), lookups, "futures first, then options");
    }

    [TestMethod]
    public async Task AnUnknownDomesticCodeIsRejectedBeforeAnythingIsSent()
    {
        using HostRig rig = await ReadyWithDomesticAsync();

        JsonElement error = (await rig.RequestAsync(1, "tw-subscribe", """{"codes":["ZZZ"]}""")).GetProperty("error");

        Assert.AreEqual("capital.subscribe.rejected", error.GetProperty("code").GetString());
        Assert.AreEqual(3023, error.GetProperty("data").GetProperty("skCode").GetInt32());
        Assert.AreEqual(0, rig.Source.LiveCallCount("tw_stocks"));
    }

    [TestMethod]
    public async Task ARejectedQuoteOrTickSubscriptionIsReported()
    {
        using HostRig rig = await ReadyWithDomesticAsync();
        rig.Source.DomesticStocksCode = 3028;

        JsonElement stocks = (await rig.RequestAsync(1, "tw-subscribe", """{"codes":["TX00"]}""")).GetProperty("error");
        Assert.AreEqual(3028, stocks.GetProperty("data").GetProperty("skCode").GetInt32());

        rig.Source.DomesticStocksCode = 0;
        rig.Source.DomesticTicksCode = 3027;
        JsonElement ticks = (await rig.RequestAsync(2, "tw-subscribe", """{"codes":["TX00"]}""")).GetProperty("error");
        Assert.AreEqual(3027, ticks.GetProperty("data").GetProperty("skCode").GetInt32());

        JsonElement kept = (await rig.RequestAsync(3, "tw-subscribe", """{"codes":["TX00"]}""")).GetProperty("result");
        CollectionAssert.AreEqual(Expected("TX00"), Strings(kept, "subscribed"), "the quote part stays subscribed");
    }

    // 依據:手冊 4-4-1「單一 SKQuoteLib 物件,僅一個訂閱 Tick 報價功能(10 檔商品)」。
    [TestMethod]
    public async Task MoreThanTenDomesticCodesIsALimitError()
    {
        using HostRig rig = await ReadyWithDomesticAsync();
        string codes = string.Join(',', Enumerable.Range(1, 11).Select(i => $"\"C{i}\""));

        JsonElement error = (await rig.RequestAsync(1, "tw-subscribe", $$"""{"codes":[{{codes}}]}""")).GetProperty("error");

        Assert.AreEqual("capital.subscribe.limit_exceeded", error.GetProperty("code").GetString());
        Assert.AreEqual(10, error.GetProperty("data").GetProperty("limit").GetInt32());
        Assert.AreEqual(0, rig.Source.LiveCallCount("tw_lookup"));
    }

    [TestMethod]
    [DataRow("""{"codes":[]}""")]
    [DataRow("""{"codes":["TX 00"]}""")]
    [DataRow("""{"codes":"TX00"}""")]
    [DataRow("""{}""")]
    public async Task AnInvalidDomesticSubscribeIsRejected(string parameters)
    {
        using HostRig rig = await ReadyWithDomesticAsync();

        JsonElement error = (await rig.RequestAsync(1, "tw-subscribe", parameters)).GetProperty("error");

        Assert.AreEqual("capital.request.invalid", error.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task ASubscribeDuringTheFirstDomesticConnectWaitsForTheOutcome()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticAutoReady = false;
        _ = await rig.StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("connecting"), Wait);

        rig.Send("""{"id":1,"method":"tw-subscribe","params":{"codes":["TX00"]}}""");
        await Task.Delay(100);
        Assert.IsFalse(rig.Output.Messages.Any(message => message.TryGetProperty("id", out _)), "held until the domestic side has an outcome");

        rig.Source.RaiseDomesticConnection(3003);
        JsonElement result = await rig.Output.WaitForAsync(message => message.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number && id.GetInt64() == 1, Wait);

        CollectionAssert.AreEqual(Expected("TX00"), Strings(result.GetProperty("result"), "subscribed"));
    }

    [TestMethod]
    public async Task ADomesticReconnectResendsTheWholeSetAndLooksTheIndexesUpAgain()
    {
        using HostRig rig = await ReadyWithDomesticAsync();
        _ = await rig.RequestAsync(1, "tw-subscribe", """{"codes":["TX00","MTX00"]}""");

        rig.Source.RaiseDomesticConnection(3002);
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("disconnected"), Wait);

        // 斷線期間的新訂閱先記住、不送。A subscription while disconnected is remembered, not sent.
        JsonElement remembered = (await rig.RequestAsync(2, "tw-subscribe", """{"codes":["TXO39800K6"]}""")).GetProperty("result");
        CollectionAssert.AreEqual(Expected("TX00", "MTX00", "TXO39800K6"), Strings(remembered, "subscribed"));

        Assert.IsTrue(await WaitUntilAsync(() => rig.Source.LiveCallCount("tw_stocks") >= 2), "re-sent after the reconnect");
        string[] calls = rig.Source.LiveCalls;
        Assert.AreEqual("tw_stocks TX00,MTX00,TXO39800K6", calls.Last(call => call.StartsWith("tw_stocks", StringComparison.Ordinal)));
        CollectionAssert.Contains(calls, "tw_ticks 2 TXO39800K6");
        Assert.AreEqual(2, calls.Count(call => call == "tw_lookup 2 TX00"), "looked up again after the reconnect");
    }

    [TestMethod]
    public async Task DomesticSubscribeWhileUnavailableIsRejected()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.DomesticEnterCode = 9999;
        _ = await rig.StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("unavailable"), Wait);

        JsonElement subscribe = (await rig.RequestAsync(1, "tw-subscribe", """{"codes":["TX00"]}""")).GetProperty("error");
        JsonElement products = (await rig.RequestAsync(2, "tw-products", """{"market":2}""")).GetProperty("error");

        Assert.AreEqual("capital.domestic.unavailable", subscribe.GetProperty("code").GetString());
        Assert.AreEqual("capital.domestic.unavailable", products.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task DomesticEventsCarryTheSubscribedCodeAndDecimals()
    {
        using HostRig rig = await ReadyWithDomesticAsync();
        _ = await rig.RequestAsync(1, "tw-subscribe", """{"codes":["TX00"]}""");

        rig.Source.RaiseDomesticQuote(2, 10);
        rig.Source.RaiseDomesticTick(new SkDomesticTick(2, 10, 7, 20261106, 84601, 123456, 3978500, 3978600, 3978600, 2, 0, true));
        rig.Source.RaiseDomesticBest5(new SkBook(2, 10, [5, 4, 3, 2, 1], [1, 2, 3, 4, 5], [6, 7, 8, 9, 10], [1, 1, 1, 1, 1], (0, 0, 11, 2), 1));
        rig.Source.RaiseDomesticLiveBar(new SkLiveBar(1, "TX00", 20261106, 846, 1, 2, 3, 4, 5));
        rig.Source.RaiseDomesticLiveBar(new SkLiveBar(1, "UNKNOWN", 20261106, 846, 1, 2, 3, 4, 5));

        JsonElement quote = (await rig.Output.WaitForAsync(HostRig.IsEvent("tw-quote"), Wait)).GetProperty("data");
        JsonElement tick = (await rig.Output.WaitForAsync(HostRig.IsEvent("tw-tick"), Wait)).GetProperty("data");
        JsonElement best5 = (await rig.Output.WaitForAsync(HostRig.IsEvent("tw-best5"), Wait)).GetProperty("data");
        JsonElement[] bars = [.. rig.Output.Messages.Where(HostRig.IsEvent("tw-live-bar")).Select(message => message.GetProperty("data"))];

        Assert.AreEqual("TX00", quote.GetProperty("code").GetString());
        Assert.AreEqual(3978600, quote.GetProperty("close").GetInt64());
        Assert.AreEqual(2, quote.GetProperty("decimals").GetInt32());
        Assert.AreEqual("TX00", tick.GetProperty("code").GetString());
        Assert.AreEqual(123456, tick.GetProperty("micros").GetInt32());
        Assert.IsTrue(tick.GetProperty("history").GetBoolean());
        Assert.AreEqual(11, best5.GetProperty("extendAsk").GetInt64());
        Assert.AreEqual(1, best5.GetProperty("simulate").GetInt32());
        Assert.AreEqual(5, best5.GetProperty("bidPrices").GetArrayLength());
        Assert.AreEqual(2, bars.Length);
        Assert.AreEqual(2, bars[0].GetProperty("decimals").GetInt32());
        Assert.IsFalse(bars[1].TryGetProperty("decimals", out _), "unknown decimals are omitted, never guessed");
    }

    [TestMethod]
    public async Task ATickForAnUnknownIndexIsLookedUpOnce()
    {
        using HostRig rig = await ReadyWithDomesticAsync();

        rig.Source.RaiseDomesticTick(new SkDomesticTick(2, 11, 1, 20261106, 84601, 0, 1, 1, 1, 1, 0, false));
        rig.Source.RaiseDomesticTick(new SkDomesticTick(2, 11, 2, 20261106, 84602, 0, 1, 1, 1, 1, 0, false));
        rig.Source.RaiseDomesticTick(new SkDomesticTick(2, 99, 3, 20261106, 84602, 0, 1, 1, 1, 1, 0, false));

        Assert.IsTrue(await WaitUntilAsync(() => rig.Output.Messages.Count(HostRig.IsEvent("tw-tick")) == 2));
        Assert.AreEqual("MTX00", rig.Output.Messages.First(HostRig.IsEvent("tw-tick")).GetProperty("data").GetProperty("code").GetString());
        Assert.AreEqual(1, rig.Source.LiveCallCount("tw_index 2 11"), "cached after the first lookup");
    }

    // 依據:手冊 4-4-h,一則含多筆(; 分隔),「##」開頭那則是結尾;別的市場的那則要丟掉。
    [TestMethod]
    public async Task TwProductsCollectsTheAskedMarketUntilTheEndMarker()
    {
        using HostRig rig = await ReadyWithDomesticAsync();

        JsonElement result = (await rig.RequestAsync(1, "tw-products", """{"market":2}""")).GetProperty("result");

        Assert.AreEqual(2, result.GetProperty("market").GetInt32());
        CollectionAssert.AreEqual(
            Expected("TX00,台指近,20261118,TXFK6,1,NTD", "MTX00,小台近,20261118,MXFK6,1,NTD"),
            Strings(result, "rows"));
    }

    [TestMethod]
    public async Task TwProductsWithoutTheEndMarkerTimesOut()
    {
        using HostRig rig = await ReadyWithDomesticAsync();
        rig.Source.StockListChunks.RemoveAt(2);

        JsonElement error = (await rig.RequestAsync(1, "tw-products", """{"market":2}""")).GetProperty("error");

        Assert.AreEqual("capital.products.timeout", error.GetProperty("code").GetString());
        Assert.AreEqual(2, error.GetProperty("data").GetProperty("received").GetInt32(), "two entries in the one chunk");
    }

    [TestMethod]
    [DataRow("""{"market":0}""")]
    [DataRow("""{"market":"2"}""")]
    [DataRow("""{}""")]
    public async Task TwProductsNeedsMarketTwoOrThree(string parameters)
    {
        using HostRig rig = await ReadyWithDomesticAsync();

        JsonElement error = (await rig.RequestAsync(1, "tw-products", parameters)).GetProperty("error");

        Assert.AreEqual("capital.request.invalid", error.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task DepthIsSentPerSymbolAndResentAfterAnOverseasReconnect()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        rig.Source.Quotes[143] = new SkQuoteData { ExchangeNo = "FX", StockNo = "SUSDTWD", Decimals = 3 };

        JsonElement result = (await rig.RequestAsync(1, "depth", """{"symbols":["FX,SUSDTWD","FX,SUSDTWD"]}""")).GetProperty("result");
        CollectionAssert.AreEqual(Expected("FX,SUSDTWD"), Strings(result, "depth"));
        Assert.AreEqual(1, rig.Source.CallCount("depth FX,SUSDTWD"));

        rig.Source.RaiseOverseasBest10(new SkBook(-1, 143, [.. Enumerable.Range(1, 10).Select(i => (long)i)], [.. Enumerable.Repeat(1L, 10)], [.. Enumerable.Range(11, 10).Select(i => (long)i)], [.. Enumerable.Repeat(2L, 10)]));
        rig.Source.RaiseOverseasBest5(new SkBook(-1, 143, [1, 2, 3, 4, 5], [1, 1, 1, 1, 1], [6, 7, 8, 9, 10], [1, 1, 1, 1, 1]));
        rig.Source.RaiseOverseasBest5(new SkBook(-1, 777, [1, 2, 3, 4, 5], [1, 1, 1, 1, 1], [6, 7, 8, 9, 10], [1, 1, 1, 1, 1]));

        JsonElement best10 = (await rig.Output.WaitForAsync(HostRig.IsEvent("best10"), Wait)).GetProperty("data");
        Assert.AreEqual("FX,SUSDTWD", best10.GetProperty("symbol").GetString());
        Assert.AreEqual(3, best10.GetProperty("decimals").GetInt32());
        Assert.IsFalse(best10.TryGetProperty("market", out _));
        Assert.AreEqual(1, rig.Output.Messages.Count(HostRig.IsEvent("best5")), "an unknown index is dropped");

        rig.Source.RaiseConnection(3002);
        _ = await rig.Output.WaitForAsync(HostRig.IsStatus("disconnected"), Wait);

        // 斷線中的十檔先記住。A depth request while disconnected is remembered.
        _ = await rig.RequestAsync(2, "depth", """{"symbols":["CME,NQ0000"]}""");
        Assert.IsTrue(await WaitUntilAsync(() => rig.Source.CallCount("depth FX,SUSDTWD") == 2 && rig.Source.CallCount("depth CME,NQ0000") == 1));
    }

    [TestMethod]
    public async Task ARejectedOrInvalidDepthIsReported()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        rig.Source.DepthCode = 3027;

        JsonElement rejected = (await rig.RequestAsync(1, "depth", """{"symbols":["FX,SUSDTWD"]}""")).GetProperty("error");
        JsonElement invalid = (await rig.RequestAsync(2, "depth", """{"symbols":["SUSDTWD"]}""")).GetProperty("error");

        Assert.AreEqual("capital.subscribe.rejected", rejected.GetProperty("code").GetString());
        Assert.AreEqual("capital.request.invalid", invalid.GetProperty("code").GetString());
    }

    internal static string[] Expected(params string[] values) => values;

    internal static string[] Strings(JsonElement element, string name) =>
        [.. element.GetProperty(name).EnumerateArray().Select(item => item.GetString()!)];

    internal static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        DateTime limit = DateTime.UtcNow + Wait;

        while (DateTime.UtcNow < limit)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    private static async Task<HostRig> ReadyWithDomesticAsync()
    {
        HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.Output.WaitForAsync(HostRig.IsDomestic("ready"), Wait);
        return rig;
    }
}
