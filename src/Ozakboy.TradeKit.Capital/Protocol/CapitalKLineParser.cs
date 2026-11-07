using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Protocol;

/// <summary>
/// 群益歷史 K 線文字解析。實測格式(2026-11-06):分 K <c>2026/11/02 16:01, 157.889999, 157.940002, 157.889999, 157.940002, 141</c>,
/// 日 / 週 / 月 K <c>2026/10/07, 158.089996, 158.500000, 157.860001, 158.080002, 142254</c>;順序 開、高、低、收、量。
/// 分 K 的時間是那一分鐘的結束(交易所當地時間)。
/// Parses Capital's historical K-line text. Measured formats: minute rows carry "yyyy/MM/dd HH:mm" and day, week and
/// month rows "yyyy/MM/dd", followed by open, high, low, close and volume. A minute row's time is the end of the minute
/// in the exchange's local time.
/// </summary>
internal static class CapitalKLineParser
{
    private const int FieldCount = 6;

    /// <summary>
    /// 解析整批;任何一行讀不懂整批失敗(<see cref="CapitalErrorCodes.KLineMalformed"/>),不回傳缺了幾根的結果。
    /// Parses a whole batch; any unreadable row fails the batch rather than returning a result with gaps.
    /// </summary>
    /// <param name="symbol">商品。The symbol.</param>
    /// <param name="type">K 線類型。The bar type.</param>
    /// <param name="lines">原始文字。The raw rows.</param>
    /// <param name="decimals">價格小數位數。The price decimals.</param>
    /// <returns>K 線。The bars.</returns>
    public static Result<IReadOnlyList<CapitalBar>> Parse(CapitalSymbol symbol, CapitalKLineType type, IReadOnlyList<string> lines, int decimals)
    {
        List<CapitalBar> bars = new(lines.Count);

        foreach (string line in lines)
        {
            Result<CapitalBar> bar = ParseLine(symbol, type, line, decimals);

            if (bar.IsFailure)
            {
                return bar.Error;
            }

            bars.Add(bar.GetValueOrThrow());
        }

        return bars;
    }

    /// <summary>
    /// 解析一行。
    /// Parses one row.
    /// </summary>
    /// <param name="symbol">商品。The symbol.</param>
    /// <param name="type">K 線類型。The bar type.</param>
    /// <param name="line">原始文字。The raw row.</param>
    /// <param name="decimals">價格小數位數。The price decimals.</param>
    /// <returns>一根 K。One bar.</returns>
    public static Result<CapitalBar> ParseLine(CapitalSymbol symbol, CapitalKLineType type, string line, int decimals)
    {
        string[] fields = line.Split(',', StringSplitOptions.TrimEntries);

        if (fields.Length != FieldCount)
        {
            return CapitalErrors.KLineMalformed(line, "欄位數不是 6。The row does not have 6 fields.");
        }

        if (!CapitalPriceScale.TryParseText(fields[1], decimals, out decimal open)
            || !CapitalPriceScale.TryParseText(fields[2], decimals, out decimal high)
            || !CapitalPriceScale.TryParseText(fields[3], decimals, out decimal low)
            || !CapitalPriceScale.TryParseText(fields[4], decimals, out decimal close))
        {
            return CapitalErrors.KLineMalformed(line, "價格讀不懂。A price could not be read.");
        }

        if (!long.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long volume))
        {
            return CapitalErrors.KLineMalformed(line, "量讀不懂。The volume could not be read.");
        }

        if (type == CapitalKLineType.Minute)
        {
            if (!DateTime.TryParseExact(fields[0], "yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime localEnd))
            {
                return CapitalErrors.KLineMalformed(line, "分 K 的時間不是 yyyy/MM/dd HH:mm。A minute row's time is not yyyy/MM/dd HH:mm.");
            }

            Result<DateTimeOffset> openTime = CapitalExchangeClock.MinuteBarOpenUtc(symbol.Exchange, localEnd);

            if (openTime.IsFailure)
            {
                return openTime.Error;
            }

            return new CapitalBar
            {
                Symbol = symbol,
                Type = type,
                OpenTimeUtc = openTime.GetValueOrThrow(),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume,
            };
        }

        if (!DateOnly.TryParseExact(fields[0], "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
        {
            return CapitalErrors.KLineMalformed(line, "日期不是 yyyy/MM/dd。The date is not yyyy/MM/dd.");
        }

        return new CapitalBar
        {
            Symbol = symbol,
            Type = type,
            TradingDay = day,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
        };
    }
}
