using System.Text.Json;
using Ozakboy.TradeKit.Capital.Protocol;

namespace Ozakboy.TradeKit.Capital.Tests;

[TestClass]
public sealed class CapitalWireTests
{
    // 依據:products.txt / product-detail.txt 合併後的形狀(宿主的 products 回應),日期照群益原文。
    // Source: the merged shape of products.txt and product-detail.txt, dates verbatim.
    private const string ProductsJson = """
        {"items":[
          {"exchange":"FX","exchangeName":"國際外匯","code":"SUSDTWD","name":"新台幣","lastTradeDate":"99991231","firstNoticeDate":"0","kind":2,"orderExchange":"--","orderCode":"--"},
          {"exchange":"NYM","exchangeName":"紐約商業交易所","code":"GC0000","name":"黃金熱2612","lastTradeDate":"20261229","firstNoticeDate":"20261130","kind":5,"orderExchange":"NYM","orderCode":"GC_202612"},
          {"exchange":"ASX","exchangeName":"澳洲雪梨交易所","code":"AP0000","name":"雪梨指數熱2612","lastTradeDate":"20261217","firstNoticeDate":"20261217","kind":null,"orderExchange":null,"orderCode":null},
          {"exchange":"","exchangeName":"","code":"","name":"","lastTradeDate":"","firstNoticeDate":"","kind":null,"orderExchange":null,"orderCode":null}
        ]}
        """;

    [TestMethod]
    public void ParseProducts_MapsDatesKindsAndOrderFields()
    {
        using JsonDocument document = JsonDocument.Parse(ProductsJson);

        IReadOnlyList<CapitalProduct> products = CapitalWire.ParseProducts(document.RootElement).GetValueOrThrow();

        Assert.AreEqual(3, products.Count, "the blank row is skipped");

        CapitalProduct fx = products[0];
        Assert.AreEqual(new CapitalSymbol("FX", "SUSDTWD"), fx.Symbol);
        Assert.AreEqual("國際外匯", fx.ExchangeName);
        Assert.AreEqual("新台幣", fx.Name);
        Assert.IsNull(fx.LastTradeDate, "99991231 means it never expires");
        Assert.IsNull(fx.FirstNoticeDate, "0 means none");
        Assert.AreEqual(2, fx.Kind);
        Assert.AreEqual("--", fx.OrderExchange);

        CapitalProduct gold = products[1];
        Assert.AreEqual(new DateOnly(2026, 12, 29), gold.LastTradeDate);
        Assert.AreEqual(new DateOnly(2026, 11, 30), gold.FirstNoticeDate);
        Assert.AreEqual("GC_202612", gold.OrderCode);

        Assert.IsNull(products[2].Kind, "no detail row");
        Assert.IsNull(products[2].OrderExchange);
    }

    [TestMethod]
    [DataRow("99991231", null)]
    [DataRow("0", null)]
    [DataRow("", null)]
    [DataRow("2026/12/18", null)]
    [DataRow("20261218", "2026-12-18")]
    public void ParseProductDate_TreatsSentinelsAsNull(string text, string? expected)
    {
        DateOnly? date = CapitalWire.ParseProductDate(text);

        Assert.AreEqual(expected is null ? null : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), date);
    }

    // 依據:probe.jsonl 的 SUSDTWD quote 事件(價格原始整數、sDecimal 3、交易日 20261106)。
    // Source: the probe's SUSDTWD quote event.
    [TestMethod]
    public void TryParseQuote_ScalesEveryPrice()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {"symbol":"FX,SUSDTWD","name":"新台幣","decimals":3,"denominator":1,"tradingDay":20261106,"open":33071,"high":33152,"low":32947,"close":32937,"settle":33067,"ref":33067,"bid":32937,"bidQty":0,"ask":32967,"askQty":0,"totalQty":5716,"tickQty":1}
            """);
        DateTimeOffset received = new(2026, 11, 6, 3, 42, 25, TimeSpan.Zero);

        Assert.IsTrue(CapitalWire.TryParseQuote(document.RootElement, received, out CapitalQuote? quote, out int denominator, out _));

        Assert.AreEqual(1, denominator);
        Assert.AreEqual(new CapitalSymbol("FX", "SUSDTWD"), quote.Symbol);
        Assert.AreEqual(new DateOnly(2026, 11, 6), quote.TradingDay);
        Assert.AreEqual(33.071m, quote.Open);
        Assert.AreEqual(33.152m, quote.High);
        Assert.AreEqual(32.947m, quote.Low);
        Assert.AreEqual(32.937m, quote.Close);
        Assert.AreEqual(33.067m, quote.Settle);
        Assert.AreEqual(33.067m, quote.Reference);
        Assert.AreEqual(32.937m, quote.Bid);
        Assert.AreEqual(32.967m, quote.Ask);
        Assert.AreEqual(5716L, quote.TotalQuantity);
        Assert.AreEqual(received, quote.ReceivedUtc);
    }

    [TestMethod]
    [DataRow("""{"symbol":"FXSUSDTWD","decimals":3,"tradingDay":20261106}""")]
    [DataRow("""{"symbol":"FX,SUSDTWD","decimals":12,"tradingDay":20261106}""")]
    [DataRow("""{"symbol":"FX,SUSDTWD","decimals":3,"tradingDay":0}""")]
    public void TryParseQuote_RefusesUnreadableQuotes(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.IsFalse(CapitalWire.TryParseQuote(document.RootElement, DateTimeOffset.UnixEpoch, out _, out _, out string problem));
        Assert.AreNotEqual(string.Empty, problem);
    }

    // 依據:ticks.txt 的 CME 索引 310(價格 2696450、sDecimal 2、芝加哥時間)。Source: CME index 310 in ticks.txt.
    [TestMethod]
    public void TryParseTick_ConvertsPriceAndTime()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {"symbol":"CME,NQ0000","ptr":81874,"date":"20261105","time":"214356","price":2696450,"qty":1,"history":true,"decimals":2}
            """);

        Assert.IsTrue(CapitalWire.TryParseTick(document.RootElement, out CapitalTick? tick, out _));

        Assert.AreEqual(81874L, tick.Sequence);
        Assert.AreEqual(26964.50m, tick.Price);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 3, 43, 56, TimeSpan.Zero), tick.TimeUtc);
        Assert.IsTrue(tick.IsReplay);
    }

    [TestMethod]
    public void TryParseTick_UnknownExchange_ReportsItsCode()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {"symbol":"INDEX,NI225","ptr":1,"date":"20261105","time":"100000","price":1,"qty":1,"history":false,"decimals":2}
            """);

        Assert.IsFalse(CapitalWire.TryParseTick(document.RootElement, out _, out Error? error));
        Assert.AreEqual(CapitalErrorCodes.UnknownExchange, error.Code);
    }
}
