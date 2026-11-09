using System.Text.Json;
using Ozakboy.TradeKit.Capital.Protocol;
using Ozakboy.TradeKit.Capital.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Tests;

/// <summary>
/// 0.3.0 事件解析:國內報價 / 成交 / 五檔 / 即時分 K、海外與海外選擇權的五檔 / 十檔、海外選擇權報價與成交、商品清單、SKCOM 版本。
/// 價格依手冊「原始整數 ÷ 10^小數位數」(4-4-u 的例子:台指期 4557900 → 45579.00),國內時間當台北時間轉 UTC。名稱含 Measured 的測試
/// 用 2026-11-09 probe-full 的實測樣本(原文照抄,宿主欄位名換成協定名)做回歸。
/// The 0.3.0 event parsing; prices follow the manual's raw-integer rule and domestic times are read as Taipei time.
/// Tests named Measured replay samples from the 2026-11-09 probe-full run.
/// </summary>
[TestClass]
public sealed class CapitalWireLiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 1, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ADomesticQuoteIsScaledByItsDecimals()
    {
        JsonElement data = Json("""
            {"code":"TX00","market":2,"name":"台指近","decimals":2,"open":4557900,"high":4560000,"low":4550000,"close":4557900,"ref":4550000,
             "bid":4557800,"bidQty":3,"ask":4557900,"askQty":4,"tickQty":1,"totalQty":1000,"yesterdayQty":900,"buyQty":600,"sellQty":400,
             "openInterest":80000,"up":5005000,"down":4095000,"simulate":1,"tradingDay":20261106,"dealTime":84601}
            """);

        Assert.IsTrue(CapitalWire.TryParseDomesticQuote(data, Now, out CapitalDomesticQuote? quote, out _));

        Assert.AreEqual(45579.00m, quote.Close, "manual 4-4-u: 4557900 is 45579.00");
        Assert.AreEqual(new CapitalSymbol(CapitalSymbol.DomesticExchange, "TX00"), quote.Symbol);
        Assert.AreEqual(CapitalDomesticMarket.Futures, quote.Market);
        Assert.AreEqual(50050.00m, quote.LimitUp);
        Assert.AreEqual(80000L, quote.OpenInterest);
        Assert.IsTrue(quote.IsSimulated);
        Assert.AreEqual(new DateOnly(2026, 11, 6), quote.TradingDay);
        Assert.AreEqual(new TimeOnly(8, 46, 1), quote.DealTime);
        Assert.AreEqual(Now, quote.ReceivedUtc);
    }

    [TestMethod]
    public void ADomesticQuoteWithoutDecimalsOrDealTimeIsHandled()
    {
        Assert.IsFalse(CapitalWire.TryParseDomesticQuote(Json("""{"code":"TX00","decimals":12}"""), Now, out _, out string problem));
        StringAssert.Contains(problem, "decimals");

        Assert.IsTrue(CapitalWire.TryParseDomesticQuote(Json("""{"code":"TX00","decimals":0,"dealTime":999999,"tradingDay":0}"""), Now, out CapitalDomesticQuote? quote, out _));
        Assert.IsNull(quote.DealTime);
        Assert.IsNull(quote.TradingDay);
    }

    // 依據:手冊 5-6,nTimemillismicros 前三位毫秒、後三位微秒(例:996886)。
    [TestMethod]
    public void ADomesticTickIsTaipeiTimeWithMicroseconds()
    {
        JsonElement data = Json("""{"code":"MTX00","market":2,"ptr":12,"date":20261106,"time":84500,"micros":996886,"bid":3978500,"ask":3978600,"close":3978600,"qty":5,"simulate":1,"history":true,"decimals":2}""");

        Assert.IsTrue(CapitalWire.TryParseDomesticTick(data, out CapitalDomesticTick? tick, out _));

        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 0, 45, 0, TimeSpan.Zero).AddMicroseconds(996886), tick.TimeUtc);
        Assert.AreEqual(39786.00m, tick.Price);
        Assert.AreEqual(39785.00m, tick.Bid);
        Assert.AreEqual(12L, tick.Sequence);
        Assert.AreEqual(20261106, tick.Date);
        Assert.AreEqual(84500, tick.Time);
        Assert.IsTrue(tick.IsSimulated);
        Assert.IsTrue(tick.IsReplay);
    }

    [TestMethod]
    [DataRow("""{"code":"TX00","ptr":1,"date":20261306,"time":84500,"decimals":2}""")]
    [DataRow("""{"code":"TX00","ptr":1,"date":20261106,"decimals":2}""")]
    [DataRow("""{"ptr":1,"date":20261106,"time":84500,"decimals":2}""")]
    public void AnUnreadableDomesticTickIsRejected(string json)
    {
        Assert.IsFalse(CapitalWire.TryParseDomesticTick(Json(json), out _, out string problem));
        Assert.AreNotEqual(string.Empty, problem);
    }

    [TestMethod]
    public void OutOfRangeMicrosecondsAreIgnored()
    {
        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 0, 45, 0, TimeSpan.Zero), CapitalWire.TaipeiToUtc(20261106, 84500, 1_000_000));
        Assert.IsNull(CapitalWire.TaipeiToUtc(20261106, 246000));
    }

    // 依據:手冊 4-4-u,nType 0 清盤(價格都是 0)、1 分 K、2 每筆更新;價格要自己除以小數位數。
    [TestMethod]
    public void LiveBarsAreScaledAndResetsHaveNoPrices()
    {
        Assert.IsTrue(CapitalWire.TryParseDomesticLiveBar(Json("""{"code":"TX00","type":1,"date":20261106,"time":904,"open":4557900,"high":4558000,"low":4557000,"close":4557500,"qty":12,"decimals":2}"""), out CapitalDomesticLiveBar? bar, out _));
        Assert.AreEqual(CapitalLiveBarKind.Minute, bar.Kind);
        Assert.AreEqual(45579.00m, bar.Open);
        Assert.AreEqual(12L, bar.Volume);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 1, 3, 0, TimeSpan.Zero), bar.OpenTimeUtc, "09:04 Taipei is the end of the minute, so it opened at 09:03");

        Assert.IsTrue(CapitalWire.TryParseDomesticLiveBar(Json("""{"code":"TX00","type":0,"date":0,"time":0,"open":0,"high":0,"low":0,"close":0,"qty":0,"decimals":2}"""), out CapitalDomesticLiveBar? reset, out _));
        Assert.AreEqual(CapitalLiveBarKind.Reset, reset.Kind);
        Assert.IsNull(reset.Open, "a reset's zeros are not prices");
        Assert.IsNull(reset.Volume);
        Assert.IsNull(reset.OpenTimeUtc);

        Assert.IsTrue(CapitalWire.TryParseDomesticLiveBar(Json("""{"code":"TX00","type":2,"date":20261106,"time":905,"open":1,"high":1,"low":1,"close":1,"qty":1}"""), out CapitalDomesticLiveBar? unknown, out _));
        Assert.IsNull(unknown.Close, "without decimals the price is unknown, not guessed");
        Assert.AreEqual(1L, unknown.Volume);

        Assert.IsFalse(CapitalWire.TryParseDomesticLiveBar(Json("""{"code":"TX00","type":3}"""), out _, out _));
    }

    // 依據:手冊 4-4-n(國內五檔含衍生一檔與試算旗標)、5-13(海外九位擴充整數,自行依 sDecimal 處理)。
    [TestMethod]
    public void BooksAreScaledAndOnlyDomesticOnesHaveTheExtendedLevel()
    {
        Assert.IsTrue(CapitalWire.TryParseBest5(
            Json("""{"code":"TX00","market":2,"decimals":2,"bidPrices":[5,4,3,2,1],"bidQuantities":[1,2,3,4,5],"askPrices":[6,7,8,9,10],"askQuantities":[1,1,1,1,1],"extendBid":100,"extendBidQty":7,"extendAsk":200,"extendAskQty":8,"simulate":0}"""),
            CapitalQuoteMarket.Domestic,
            Now,
            out CapitalBest5? domestic,
            out _));

        Assert.AreEqual(new CapitalBookLevel(0.05m, 1), domestic.Bids[0]);
        Assert.AreEqual(new CapitalBookLevel(1.00m, 7), domestic.ExtendedBid);
        Assert.AreEqual(new CapitalBookLevel(2.00m, 8), domestic.ExtendedAsk);
        Assert.IsFalse(domestic.IsSimulated);
        Assert.AreEqual(CapitalSymbol.DomesticExchange, domestic.Symbol.Exchange);

        Assert.IsTrue(CapitalWire.TryParseBest5(
            Json("""{"symbol":"FX,SUSDTWD","decimals":3,"bidPrices":[32937,32936,32935,32934,32933],"bidQuantities":[0,0,0,0,0],"askPrices":[32967,32968,32969,32970,32971],"askQuantities":[0,0,0,0,0]}"""),
            CapitalQuoteMarket.Overseas,
            Now,
            out CapitalBest5? overseas,
            out _));

        Assert.AreEqual(32.937m, overseas.Bids[0].Price);
        Assert.IsNull(overseas.ExtendedBid);
        Assert.IsNull(overseas.IsSimulated, "the overseas book has no simulate flag");

        string ten = string.Join(',', Enumerable.Range(1, 10));
        Assert.IsTrue(CapitalWire.TryParseBest10(Json($$"""{"symbol":"CME,NQ0000","decimals":2,"bidPrices":[{{ten}}],"bidQuantities":[{{ten}}],"askPrices":[{{ten}}],"askQuantities":[{{ten}}]}"""), CapitalQuoteMarket.OverseasOptions, Now, out CapitalBest10? best10, out _));
        Assert.HasCount(10, best10.Asks);
        Assert.AreEqual(CapitalQuoteMarket.OverseasOptions, best10.Market);
    }

    [TestMethod]
    [DataRow("""{"symbol":"FX,SUSDTWD","decimals":3,"bidPrices":[1,2,3,4],"bidQuantities":[1,2,3,4,5],"askPrices":[1,2,3,4,5],"askQuantities":[1,2,3,4,5]}""")]
    [DataRow("""{"symbol":"FX,SUSDTWD","decimals":3,"bidPrices":[1,2,3,4,"x"],"bidQuantities":[1,2,3,4,5],"askPrices":[1,2,3,4,5],"askQuantities":[1,2,3,4,5]}""")]
    [DataRow("""{"symbol":"SUSDTWD","decimals":3,"bidPrices":[1,2,3,4,5],"bidQuantities":[1,2,3,4,5],"askPrices":[1,2,3,4,5],"askQuantities":[1,2,3,4,5]}""")]
    [DataRow("""{"symbol":"FX,SUSDTWD","bidPrices":[1,2,3,4,5],"bidQuantities":[1,2,3,4,5],"askPrices":[1,2,3,4,5],"askQuantities":[1,2,3,4,5]}""")]
    public void AMalformedBookIsRejected(string json)
    {
        Assert.IsFalse(CapitalWire.TryParseBest5(Json(json), CapitalQuoteMarket.Overseas, Now, out _, out _));
        Assert.IsFalse(CapitalWire.TryParseBest10(Json(json), CapitalQuoteMarket.Overseas, Now, out _, out _));
    }

    [TestMethod]
    public void OverseasOptionQuotesAndTicksUseTheExchangeClock()
    {
        Assert.IsTrue(CapitalWire.TryParseOptionQuote(Json("""{"symbol":"CME,NQ2612C25000","name":"小那買權","exchangeName":"CME","callPut":"C","decimals":2,"tradingDay":20261106,"close":125100,"strike":2500000}"""), Now, out CapitalOverseasOptionQuote? quote, out _));
        Assert.AreEqual(1251.00m, quote.Close);
        Assert.AreEqual(2500000L, quote.StrikePriceRaw, "the strike stays raw; its decimals are unmeasured");
        Assert.AreEqual("C", quote.CallPut);

        Assert.IsTrue(CapitalWire.TryParseOptionTick(Json("""{"symbol":"CME,NQ2612C25000","ptr":1,"date":"20261105","time":"214356","price":125050,"qty":2,"history":true,"decimals":2}"""), out CapitalOverseasOptionTick? tick, out _));
        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 3, 43, 56, TimeSpan.Zero), tick.TimeUtc, "CME is Chicago time, as measured for futures");
        Assert.IsTrue(tick.IsTimeZoneVerified);
        Assert.AreEqual(1250.50m, tick.Price);

        Assert.IsTrue(CapitalWire.TryParseOptionTick(Json("""{"symbol":"ZZZ,X1","ptr":1,"date":"20261105","time":"214356","price":1,"decimals":0}"""), out CapitalOverseasOptionTick? unknown, out _));
        Assert.IsNull(unknown.TimeUtc, "an unknown exchange keeps the raw time only");
        Assert.IsFalse(unknown.IsTimeZoneVerified);
        Assert.AreEqual("214356", unknown.Time);

        Assert.IsFalse(CapitalWire.TryParseOptionTick(Json("""{"symbol":"CME,X","decimals":2}"""), out _, out _));
        Assert.IsFalse(CapitalWire.TryParseOptionQuote(Json("""{"symbol":"CME","decimals":2}"""), Now, out _, out _));
    }

    // 依據:手冊 4-4-h:[商品代碼],[名稱],[最後交易日],[交易所商品代碼],[跳動點],[幣別];最後交易日 0 = 沒有。
    // 樣本取自 2026-11-09 probe-full 的 tw-stocklist-2.txt / -3.txt(原文照抄):期貨與選擇權都是 6 欄,選擇權的系列列是 5 欄(沒有幣別)。
    [TestMethod]
    public void DomesticProductsFollowTheMeasuredColumns()
    {
        JsonElement result = Json("""
            {"market":3,"rows":[
             "TX00AM,台指近,20261118,TXFK600,100000| 1 | ,NTD",
             "TXO41500K6,台選11C,20261118,TXO41500K6,10| 0.1 | / 50| 0.5 | / 500| 1.0 | / 1000| 5.0 | / 100000| 10 | ,NTD",
             "TXO11,台選11,0,TXO11,",
             "X"]}
            """);

        IReadOnlyList<CapitalDomesticProduct> products = CapitalWire.ParseDomesticProducts(result, CapitalDomesticMarket.Options).GetValueOrThrow();

        Assert.HasCount(4, products);
        Assert.AreEqual("TX00AM", products[0].Code);
        Assert.AreEqual("台指近", products[0].Name);
        Assert.AreEqual(new DateOnly(2026, 11, 18), products[0].LastTradeDate);
        Assert.AreEqual("TXFK600", products[0].ExchangeCode);
        Assert.AreEqual("100000| 1 |", products[0].TickSize, "the fields are trimmed");
        Assert.AreEqual("NTD", products[0].Currency);
        Assert.AreEqual("10| 0.1 | / 50| 0.5 | / 500| 1.0 | / 1000| 5.0 | / 100000| 10 |", products[1].TickSize);
        Assert.AreEqual("TXO41500K6", products[1].ExchangeCode);
        Assert.IsNull(products[2].LastTradeDate, "0 means none");
        Assert.AreEqual("TXO11", products[2].ExchangeCode, "a series row keeps its fixed positions");
        Assert.IsNull(products[2].TickSize);
        Assert.IsNull(products[2].Currency, "a series row has no currency field");
        Assert.IsNull(products[3].Name);
        Assert.AreEqual(CapitalErrorCodes.ProtocolError, CapitalWire.ParseDomesticProducts(Json("{}"), CapitalDomesticMarket.Options).Failure().Code);
    }

    // 樣本取自 2026-11-09 probe-full 的 oo-products.txt(原文照抄):5 欄,最後一欄是最後交易日。
    [TestMethod]
    public void OverseasOptionProductsFollowTheMeasuredColumns()
    {
        IReadOnlyList<CapitalOverseasOptionProduct> products = CapitalWire.ParseOptionProducts(Json("""{"items":["CME,芝加哥商品交易所,ES00100X6,小SP季選2612P 100 P,20261218","Eurex,歐洲期貨交易所,P_I05790W6,藍籌50日選1112P 5790 P,20261112","CME,x,NQ 26,y"]}""")).GetValueOrThrow();

        Assert.AreEqual(new CapitalSymbol("CME", "ES00100X6"), products[0].Symbol);
        Assert.AreEqual("芝加哥商品交易所", products[0].ExchangeName);
        Assert.AreEqual("小SP季選2612P 100 P", products[0].Name);
        Assert.AreEqual(new DateOnly(2026, 12, 18), products[0].LastTradeDate);
        Assert.AreEqual(new CapitalSymbol("Eurex", "P_I05790W6"), products[1].Symbol, "an underscore in the root is a valid code");
        Assert.IsNull(products[2].Symbol, "a code with a space cannot be subscribed");
        Assert.IsNull(products[2].LastTradeDate);
        Assert.AreEqual(CapitalErrorCodes.ProtocolError, CapitalWire.ParseOptionProducts(Json("[]")).Failure().Code);
    }

    // 樣本取自 2026-11-09 probe-full 的 probe.jsonl(tw_quote,宿主欄位名改成協定名):小台訂閱後第一則只有開高低,成交價、買賣價、
    // 成交時間是 0;下一則就補齊。國內價格不可能是 0,所以 0 → null。
    [TestMethod]
    public void MeasuredPartialFirstQuoteHasNullPrices()
    {
        JsonElement first = Json("""
            {"code":"MTX00","market":2,"name":"小台近","decimals":2,"open":4124000,"high":4133600,"low":4071400,"close":0,"tickQty":0,"ref":4147800,
             "bid":0,"bidQty":0,"ask":0,"askQty":0,"buyQty":0,"sellQty":0,"openInterest":26119,"totalQty":0,"yesterdayQty":0,"up":4562500,
             "down":3733100,"simulate":0,"tradingDay":20261110,"dealTime":0}
            """);
        JsonElement next = Json("""
            {"code":"MTX00","market":2,"name":"小台近","decimals":2,"open":4124000,"high":4133600,"low":4071400,"close":4120100,"tickQty":1,"ref":4147800,
             "bid":4119500,"bidQty":2,"ask":4119800,"askQty":2,"buyQty":36889,"sellQty":31178,"openInterest":26119,"totalQty":68122,"yesterdayQty":0,"up":4562500,
             "down":3733100,"simulate":0,"tradingDay":20261110,"dealTime":200748}
            """);

        Assert.IsTrue(CapitalWire.TryParseDomesticQuote(first, Now, out CapitalDomesticQuote? partial, out _));
        Assert.IsTrue(CapitalWire.TryParseDomesticQuote(next, Now, out CapitalDomesticQuote? full, out _));

        Assert.AreEqual(41240.00m, partial.Open);
        Assert.IsNull(partial.Close, "a zero price is not a price");
        Assert.IsNull(partial.Bid);
        Assert.IsNull(partial.Ask);
        Assert.IsNull(partial.DealTime, "zero with no price is no deal time");
        Assert.AreEqual(new DateOnly(2026, 11, 10), partial.TradingDay, "a night-session quote carries the next trading day");
        Assert.AreEqual(41201.00m, full.Close);
        Assert.AreEqual(new TimeOnly(20, 7, 48), full.DealTime);
        Assert.AreEqual(45625.00m, full.LimitUp);
    }

    // 樣本取自 2026-11-09 probe-full 的 tw-ticks-TX00.txt / tw-ticks-TXO41500K6AM.txt(欄位依檔頭換成協定名):
    // 即時 20261109 201107 817000 在 UTC 12:11:07.989 收到;選擇權 08:30:50 的試撮回補列整筆是 0。
    [TestMethod]
    public void MeasuredTicksConvertFromTaipeiAndZeroPricesAreNull()
    {
        Assert.IsTrue(CapitalWire.TryParseDomesticTick(
            Json("""{"code":"TX00","market":2,"ptr":16636,"date":20261109,"time":201107,"micros":817000,"bid":4119900,"ask":4120100,"close":4120200,"qty":1,"simulate":0,"history":false,"decimals":2}"""),
            out CapitalDomesticTick? live,
            out _));
        Assert.AreEqual(new DateTimeOffset(2026, 11, 9, 12, 11, 7, 817, TimeSpan.Zero), live.TimeUtc, "nDate is the calendar day; 817000 is 817 ms");
        Assert.AreEqual(41202.00m, live.Price);
        Assert.AreEqual(817000, live.Micros);
        Assert.IsFalse(live.IsSimulated);

        Assert.IsTrue(CapitalWire.TryParseDomesticTick(
            Json("""{"code":"TXO41500K6AM","market":3,"ptr":0,"date":20261109,"time":83050,"micros":0,"bid":0,"ask":0,"close":0,"qty":0,"simulate":1,"history":true,"decimals":2}"""),
            out CapitalDomesticTick? trial,
            out _));
        Assert.IsNull(trial.Price, "a simulated replay row may carry no price at all");
        Assert.IsNull(trial.Bid);
        Assert.IsTrue(trial.IsSimulated);
        Assert.IsTrue(trial.IsReplay);
    }

    // 樣本取自 2026-11-09 probe-full 的 tw-livek-TX00.txt:夜盤第一根標 1501(開盤價 = 15:00:00.003 的第一筆成交 41240.00),
    // 20:11:07 收到的即時更新標 2012。HHmm 是那一分鐘的結束時刻。
    [TestMethod]
    public void MeasuredLiveBarsAreLabelledByTheirEndMinute()
    {
        Assert.IsTrue(CapitalWire.TryParseDomesticLiveBar(Json("""{"code":"TX00","type":1,"date":20261109,"time":1501,"open":4124000,"high":4130000,"low":4118400,"close":4123500,"qty":335,"decimals":2}"""), out CapitalDomesticLiveBar? first, out _));
        Assert.AreEqual(new DateTimeOffset(2026, 11, 9, 7, 0, 0, TimeSpan.Zero), first.OpenTimeUtc, "the 1501 bar opened at 15:00 Taipei");
        Assert.AreEqual(41240.00m, first.Open);

        Assert.IsTrue(CapitalWire.TryParseDomesticLiveBar(Json("""{"code":"TX00","type":2,"date":20261109,"time":2012,"open":4120100,"high":4120200,"low":4120100,"close":4120200,"qty":5,"decimals":2}"""), out CapitalDomesticLiveBar? forming, out _));
        Assert.AreEqual(CapitalLiveBarKind.Update, forming.Kind);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 9, 12, 11, 0, TimeSpan.Zero), forming.OpenTimeUtc, "an update received at 20:11:07 belongs to the 20:11 minute");
    }

    // 樣本取自 2026-11-09 probe-full 的 probe.jsonl(oo_quote):ES 深度價內買權當天沒有成交,結算 730200(sDecimal 2 → 7302.00),
    // 履約價 100 與名稱「小SP季選2612C 100 C」一致、沒有放大。
    [TestMethod]
    public void MeasuredOverseasOptionQuoteScalesPricesButNotTheStrike()
    {
        Assert.IsTrue(CapitalWire.TryParseOptionQuote(
            Json("""{"symbol":"CME,ES00100L6","name":"小SP季選2612C","exchangeName":"芝加哥商品交易所","callPut":"C","decimals":2,"tradingDay":20261109,"open":0,"high":0,"low":0,"close":0,"settle":730200,"ref":730200,"bid":0,"bidQty":0,"ask":0,"askQty":0,"totalQty":0,"strike":100}"""),
            Now,
            out CapitalOverseasOptionQuote? quote,
            out _));

        Assert.AreEqual(7302.00m, quote.Settle);
        Assert.AreEqual(7302.00m, quote.Reference);
        Assert.IsNull(quote.Close, "no trade today");
        Assert.IsNull(quote.Bid, "no bid");
        Assert.AreEqual(100L, quote.StrikePriceRaw);
    }

    [TestMethod]
    public void TheStatusCarriesTheVersionAndTheOptionFields()
    {
        HostStatus status = CapitalWire.ParseStatus(Json("""{"state":"ready","skcomVersion":"2.13.59_x64","overseasOptions":"unavailable","overseasOptionsReason":"enter_rejected","overseasOptionsCode":2026}"""));

        Assert.AreEqual("2.13.59_x64", status.SkcomVersion);
        Assert.AreEqual("unavailable", status.OverseasOptions);
        Assert.AreEqual(2026, status.OverseasOptionsCode);
        Assert.IsTrue(CapitalWire.TryParseOverseasOptionsState("disconnected", out CapitalOverseasOptionsState state));
        Assert.AreEqual(CapitalOverseasOptionsState.Disconnected, state);
        Assert.IsFalse(CapitalWire.TryParseOverseasOptionsState("weird", out _));
    }

    [TestMethod]
    [DataRow("2.13.59_x64", true)]
    [DataRow(" 2.13.59_X64 ", true)]
    [DataRow("2.13.60_x64", false)]
    [DataRow("2.13.59_x86", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void OnlyTheMeasuredSkcomVersionIsTested(string? version, bool tested)
    {
        Assert.AreEqual(tested, CapitalSkcomVersion.IsTested(version));
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
