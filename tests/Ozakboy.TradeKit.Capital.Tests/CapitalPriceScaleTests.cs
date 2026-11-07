namespace Ozakboy.TradeKit.Capital.Tests;

[TestClass]
public sealed class CapitalPriceScaleTests
{
    // 依據:probe.jsonl 的 quote 事件(USD/TWD sDecimal 3、JPY 2、EUR 4、NQ 2、GC 1、YM 0)。
    // Source: the probe's quote events.
    [TestMethod]
    [DataRow(32937L, 3, "32.937")]
    [DataRow(15786L, 2, "157.86")]
    [DataRow(11200L, 4, "1.1200")]
    [DataRow(2698375L, 2, "26983.75")]
    [DataRow(40518L, 1, "4051.8")]
    [DataRow(51176L, 0, "51176")]
    [DataRow(-15L, 2, "-0.15")]
    [DataRow(0L, 4, "0.0000")]
    [DataRow(123456789L, 9, "0.123456789")]
    public void FromRaw_DividesByPowerOfTenWithTheExactScale(long raw, int decimals, string expected)
    {
        decimal value = CapitalPriceScale.FromRaw(raw, decimals);

        Assert.AreEqual(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
        Assert.AreEqual(expected, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void FromRaw_HandlesTheExtremes()
    {
        Assert.AreEqual(long.MaxValue, (long)CapitalPriceScale.FromRaw(long.MaxValue, 0));
        Assert.AreEqual(long.MinValue, (long)CapitalPriceScale.FromRaw(long.MinValue, 0));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(10)]
    public void FromRaw_RejectsDecimalsOutsideZeroToNine(int decimals)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CapitalPriceScale.FromRaw(1, decimals));
    }

    // 依據:kline.txt 的單精度浮點文字(157.889999 是 157.89 的 float 印出來的樣子)。
    // Source: kline.txt, where 157.889999 is how the float for 157.89 prints.
    [TestMethod]
    [DataRow("157.889999", 2, "157.89")]
    [DataRow("157.940002", 2, "157.94")]
    [DataRow("32.598999", 3, "32.599")]
    [DataRow("28212.750000", 2, "28212.75")]
    [DataRow("158.500000", 2, "158.50")]
    [DataRow("1.005", 2, "1.01")]
    [DataRow("-1.005", 2, "-1.01")]
    public void TryParseText_RoundsAwayFromZeroToTheDecimals(string text, int decimals, string expected)
    {
        Assert.IsTrue(CapitalPriceScale.TryParseText(text, decimals, out decimal value));
        Assert.AreEqual(expected, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("abc")]
    [DataRow("1e5")]
    [DataRow("1,5")]
    public void TryParseText_RefusesNonDecimalText(string text)
    {
        Assert.IsFalse(CapitalPriceScale.TryParseText(text, 2, out _));
    }

    [TestMethod]
    public void TryParseText_RefusesDecimalsOutOfRange()
    {
        Assert.IsFalse(CapitalPriceScale.TryParseText("1.0", 10, out _));
        Assert.IsFalse(CapitalPriceScale.TryParseText("1.0", -1, out _));
    }
}
