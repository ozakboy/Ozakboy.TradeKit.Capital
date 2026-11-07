using System.Globalization;

namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 群益價格換算。報價與成交的價格是原始整數,÷ 10^小數位數即為 decimal(整個過程不經過 double / float);K 線的價格是
/// 單精度浮點印出來的文字(<c>157.889999</c>),先以 decimal 讀,再依小數位數四捨五入(<c>157.89</c>)。
/// Capital's price conversion. Quote and tick prices are raw integers divided by 10^decimals, in decimal throughout
/// with no double or float; K-line prices are single-precision floats printed as text (157.889999), read as decimal and
/// rounded to the decimals (157.89).
/// </summary>
/// <remarks>
/// 實測(2026-11-06):<c>nDenominator</c> 一律是 1;不是 1 的商品(分數報價)本套件不支援,照小數位數換算並記警告
/// <see cref="CapitalErrorCodes.PriceFractionUnsupported"/>。
/// Measured: nDenominator was always 1. Fractional quotes are unsupported; they are scaled by decimals anyway with the
/// <see cref="CapitalErrorCodes.PriceFractionUnsupported"/> warning.
/// </remarks>
public static class CapitalPriceScale
{
    /// <summary>支援的最大小數位數(九位小數 API)。The largest supported number of decimals (the nine-digit API).</summary>
    public const int MaxDecimals = 9;

    /// <summary>
    /// 原始整數 → decimal:<paramref name="raw"/> ÷ 10^<paramref name="decimals"/>,結果的 scale 就是 <paramref name="decimals"/>。
    /// Raw integer to decimal: raw divided by 10^decimals, with exactly that scale.
    /// </summary>
    /// <param name="raw">原始整數價格。The raw integer price.</param>
    /// <param name="decimals">小數位數(0–9)。Decimals, 0 to 9.</param>
    /// <returns>價格。The price.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="decimals"/> 不在 0–9。Decimals out of 0-9.</exception>
    public static decimal FromRaw(long raw, int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, MaxDecimals);

        ulong magnitude = raw < 0 ? (ulong)(-(raw + 1)) + 1 : (ulong)raw;
        return new decimal((int)(uint)magnitude, (int)(uint)(magnitude >> 32), 0, raw < 0, (byte)decimals);
    }

    /// <summary>
    /// K 線文字價格 → decimal:<c>decimal.Parse(InvariantCulture)</c> 後 <c>Math.Round(值, decimals, AwayFromZero)</c>。
    /// A K-line text price to decimal: parsed with the invariant culture, then rounded away from zero to the decimals.
    /// </summary>
    /// <param name="text">文字,例如 <c>157.889999</c>。The text, for example 157.889999.</param>
    /// <param name="decimals">小數位數(0–9)。Decimals, 0 to 9.</param>
    /// <param name="value">結果。The result.</param>
    /// <returns>讀得懂時為 <see langword="true"/>。True when readable.</returns>
    public static bool TryParseText(string? text, int decimals, out decimal value)
    {
        value = 0m;

        if (decimals is < 0 or > MaxDecimals || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal parsed))
        {
            return false;
        }

        value = Math.Round(parsed, decimals, MidpointRounding.AwayFromZero);
        return true;
    }
}
