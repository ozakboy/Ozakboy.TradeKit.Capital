using Ozakboy.TradeKit.Capital.Protocol;

namespace Ozakboy.TradeKit.Capital.Tests;

[TestClass]
public sealed class CapitalKLineParserTests
{
    private static readonly CapitalSymbol UsdJpy = new("FX", "SUSDJPY");

    // 依據:kline.txt(SKCOM 2.13.59,2026-11-06 探針)。Source: kline.txt from the probe.
    [TestMethod]
    public void MinuteRow_IsRoundedAndOpensOneMinuteBeforeItsTime()
    {
        CapitalBar bar = CapitalKLineParser
            .ParseLine(UsdJpy, CapitalKLineType.Minute, "2026/11/02 16:01, 157.889999, 157.940002, 157.889999, 157.940002, 141", 2)
            .GetValueOrThrow();

        Assert.AreEqual(new DateTimeOffset(2026, 11, 2, 21, 0, 0, TimeSpan.Zero), bar.OpenTimeUtc);
        Assert.IsNull(bar.TradingDay);
        Assert.AreEqual(157.89m, bar.Open);
        Assert.AreEqual(157.94m, bar.High);
        Assert.AreEqual(157.89m, bar.Low);
        Assert.AreEqual(157.94m, bar.Close);
        Assert.AreEqual(141L, bar.Volume);
        Assert.AreEqual(UsdJpy, bar.Symbol);
    }

    [TestMethod]
    public void CmeMinuteRow_UsesChicagoTime()
    {
        CapitalBar bar = CapitalKLineParser
            .ParseLine(new CapitalSymbol("CME", "NQ0000"), CapitalKLineType.Minute, "2026/11/02 17:01, 28201.000000, 28212.750000, 28165.000000, 28203.000000, 679", 2)
            .GetValueOrThrow();

        // 芝加哥 17:01 標準時間(UTC−6)= 23:01Z,開始 23:00Z。Chicago 17:01 standard time is 23:01Z; open 23:00Z.
        Assert.AreEqual(new DateTimeOffset(2026, 11, 2, 23, 0, 0, TimeSpan.Zero), bar.OpenTimeUtc);
        Assert.AreEqual(28212.75m, bar.High);
    }

    [TestMethod]
    public void DailyRow_HasOnlyTheTradingDay()
    {
        CapitalBar bar = CapitalKLineParser
            .ParseLine(UsdJpy, CapitalKLineType.Day, "2026/10/07, 158.089996, 158.500000, 157.860001, 158.080002, 142254", 2)
            .GetValueOrThrow();

        Assert.AreEqual(new DateOnly(2026, 10, 7), bar.TradingDay);
        Assert.IsNull(bar.OpenTimeUtc);
        Assert.AreEqual(158.09m, bar.Open);
        Assert.AreEqual(158.50m, bar.High);
        Assert.AreEqual(157.86m, bar.Low);
        Assert.AreEqual(158.08m, bar.Close);
        Assert.AreEqual(142254L, bar.Volume);
    }

    [TestMethod]
    [DataRow("2026/11/02 16:01, 157.889999, 157.940002, 157.889999, 157.940002")]
    [DataRow("2026/11/02, 157.889999, 157.940002, 157.889999, 157.940002, 141")]
    [DataRow("2026/11/02 16:01, abc, 157.940002, 157.889999, 157.940002, 141")]
    [DataRow("2026/11/02 16:01, 157.889999, 157.940002, 157.889999, 157.940002, 1.5")]
    public void MalformedMinuteRow_FailsTheWholeBatch(string line)
    {
        Result<IReadOnlyList<CapitalBar>> result = CapitalKLineParser.Parse(
            UsdJpy,
            CapitalKLineType.Minute,
            ["2026/11/02 16:02, 157.919998, 157.940002, 157.910004, 157.919998, 108", line],
            2);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(CapitalErrorCodes.KLineMalformed, result.Error.Code);
    }

    [TestMethod]
    public void EmptyBatch_IsAnEmptySuccess()
    {
        Result<IReadOnlyList<CapitalBar>> result = CapitalKLineParser.Parse(UsdJpy, CapitalKLineType.Minute, [], 2);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.GetValueOrThrow().Count);
    }
}
