using System.Globalization;
using Ozakboy.TradeKit.Capital.Protocol;

namespace Ozakboy.TradeKit.Capital.Tests;

/// <summary>
/// 國內 K 線解析。依據 2026-11-07 <c>probe-tw</c> 實測:分 K <c>yyyy/MM/dd HH:mm, O, H, L, C, V</c>(那一分鐘的結束、台北時間、
/// 日期是日曆日),日 K <c>yyyy/MM/dd, O, H, L, C, V</c>,價格是 <c>45395.00</c> 這種小數字串。下面的價格數字是格式範例,不是實測值。
/// Domestic K-line parsing, by the 2026-11-07 probe-tw formats; the prices below are format samples, not measured values.
/// </summary>
[TestClass]
public sealed class CapitalDomesticKLineParserTests
{
    [TestMethod]
    public void MinuteRowEndsInTaipeiAndOpensOneMinuteEarlierInUtc()
    {
        Assert.IsTrue(CapitalDomesticKLineParser.TryParseLine("TX00", CapitalDomesticKLineType.Minute, "2026/11/06 13:45, 45395.00, 45410.00, 45380.00, 45390.00, 812", out CapitalDomesticBar? bar));

        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 5, 44, 0, TimeSpan.Zero), bar.OpenTimeUtc, "13:45 Taipei is the end; 13:44 Taipei = 05:44Z");
        Assert.IsNull(bar.TradingDay);
        Assert.AreEqual("TX00", bar.Code);
        Assert.AreEqual(CapitalDomesticKLineType.Minute, bar.Type);
        Assert.AreEqual(45395.00m, bar.Open);
        Assert.AreEqual(45410.00m, bar.High);
        Assert.AreEqual(45380.00m, bar.Low);
        Assert.AreEqual(45390.00m, bar.Close);
        Assert.AreEqual(812L, bar.Volume);
    }

    [TestMethod]
    public void PricesAreReadAsDecimalTextWithoutRounding()
    {
        Assert.IsTrue(CapitalDomesticKLineParser.TryParseLine("TX00", CapitalDomesticKLineType.Minute, "2026/11/06 13:45, 45395.00, 45395.125, 0.1, 45395.10, 1", out CapitalDomesticBar? bar));

        Assert.AreEqual("45395.00", bar.Open.ToString(CultureInfo.InvariantCulture), "the scale is kept");
        Assert.AreEqual(45395.125m, bar.High, "no rounding to two places");
        Assert.AreEqual(0.1m, bar.Low, "0.1 is exact, as it never passes through double");
    }

    [TestMethod]
    public void TheMidnightBarBelongsToTheNewCalendarDay()
    {
        Assert.IsTrue(CapitalDomesticKLineParser.TryParseLine("TX00", CapitalDomesticKLineType.Minute, "2026/11/03 00:00, 1.00, 1.00, 1.00, 1.00, 3", out CapitalDomesticBar? bar));

        // 00:00(11/03)的開始是 11/02 23:59 台北 = 11/02 15:59Z。The 00:00 bar opens at 23:59 Taipei of the day before.
        Assert.AreEqual(new DateTimeOffset(2026, 11, 2, 15, 59, 0, TimeSpan.Zero), bar.OpenTimeUtc);
    }

    [TestMethod]
    public void TheFirstNightBarOpensAtFifteenHundredTaipei()
    {
        Assert.IsTrue(CapitalDomesticKLineParser.TryParseLine("TX00", CapitalDomesticKLineType.Minute, "2026/10/30 15:01, 1.00, 1.00, 1.00, 1.00, 3", out CapitalDomesticBar? bar));

        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.Zero), bar.OpenTimeUtc);
    }

    [TestMethod]
    public void DailyRowCarriesOnlyTheTradingDay()
    {
        Assert.IsTrue(CapitalDomesticKLineParser.TryParseLine("MTX00", CapitalDomesticKLineType.Day, "2026/11/02, 45000.00, 45500.00, 44900.00, 45395.00, 98213", out CapitalDomesticBar? bar));

        Assert.AreEqual(new DateOnly(2026, 11, 2), bar.TradingDay);
        Assert.IsNull(bar.OpenTimeUtc);
        Assert.AreEqual(98213L, bar.Volume);
    }

    // 依據:實測會給量為 0 的空 K 棒(沒有成交的分鐘),套件不過濾。Measured: empty zero-volume bars come through and are kept.
    [TestMethod]
    public void AnEmptyBarIsKeptWithZeroVolume()
    {
        IReadOnlyList<CapitalDomesticBar> bars = CapitalDomesticKLineParser.Parse(
            "TM0000",
            CapitalDomesticKLineType.Minute,
            ["2026/11/06 09:01, 45395.00, 45395.00, 45395.00, 45395.00, 0"],
            out int skipped);

        Assert.AreEqual(0, skipped);
        Assert.AreEqual(1, bars.Count);
        Assert.AreEqual(0L, bars[0].Volume);
    }

    [TestMethod]
    public void MalformedRowsAreCountedAndSkippedWithoutFailingTheBatch()
    {
        string[] lines =
        [
            "2026/11/06 08:46, 1.00, 2.00, 0.50, 1.50, 10",
            "2026/11/06 08:47, 1.00, 2.00, 0.50, 1.50",
            "2026/11/06 08:48, abc, 2.00, 0.50, 1.50, 10",
            "2026-11-06 08:49, 1.00, 2.00, 0.50, 1.50, 10",
            "2026/11/06, 1.00, 2.00, 0.50, 1.50, 10",
            "2026/11/06 08:50, 1.00, 2.00, 0.50, 1.50, -1",
            "2026/11/06 08:51, 1.00, 2.00, 0.50, 1.50, 1.5",
            "2026/11/06 08:52, 1,000.00, 2.00, 0.50, 1.50, 10",
            string.Empty,
            "2026/11/06 08:53, 1.00, 2.00, 0.50, 1.50, 11",
        ];

        IReadOnlyList<CapitalDomesticBar> bars = CapitalDomesticKLineParser.Parse("TX00", CapitalDomesticKLineType.Minute, lines, out int skipped);

        Assert.AreEqual(8, skipped);
        Assert.AreEqual(2, bars.Count);
        Assert.AreEqual(10L, bars[0].Volume, "order is kept");
        Assert.AreEqual(11L, bars[1].Volume);
    }

    [TestMethod]
    public void AMinuteRowAskedAsDailyIsSkipped()
    {
        Assert.IsFalse(CapitalDomesticKLineParser.TryParseLine("TX00", CapitalDomesticKLineType.Day, "2026/11/06 13:45, 1.00, 1.00, 1.00, 1.00, 1", out _));
        Assert.IsFalse(CapitalDomesticKLineParser.TryParseLine("TX00", CapitalDomesticKLineType.Minute, null, out _));
    }

    [TestMethod]
    [DataRow("connecting", CapitalDomesticState.Connecting)]
    [DataRow("ready", CapitalDomesticState.Ready)]
    [DataRow("disconnected", CapitalDomesticState.Disconnected)]
    [DataRow("unavailable", CapitalDomesticState.Unavailable)]
    public void DomesticWireStatesMapToTheEnum(string wire, CapitalDomesticState expected)
    {
        Assert.IsTrue(CapitalWire.TryParseDomesticState(wire, out CapitalDomesticState state));
        Assert.AreEqual(expected, state);
    }

    [TestMethod]
    public void AnUnknownDomesticWireStateIsNotMapped()
    {
        Assert.IsFalse(CapitalWire.TryParseDomesticState("sleeping", out CapitalDomesticState state));
        Assert.AreEqual(CapitalDomesticState.NotStarted, state);
        Assert.IsFalse(CapitalWire.TryParseDomesticState(null, out _));
    }

    [TestMethod]
    [DataRow("TX00", true)]
    [DataRow("TM0000", true)]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow("TX,00", false)]
    [DataRow("TX 00", false)]
    public void DomesticCodesFollowTheHostsRule(string? code, bool valid) =>
        Assert.AreEqual(valid, CapitalHostSession.IsValidDomesticCode(code));
}
