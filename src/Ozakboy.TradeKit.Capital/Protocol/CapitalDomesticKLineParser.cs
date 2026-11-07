using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Protocol;

/// <summary>
/// 群益國內歷史 K 線文字解析(<c>SKQuoteLib_RequestKLineAMByDate</c>,sOutType = 1 新版格式)。實測格式(2026-11-07):
/// 分 K <c>yyyy/MM/dd HH:mm, O, H, L, C, V</c>,日 K <c>yyyy/MM/dd, O, H, L, C, V</c>;
/// 順序 開、高、低、收、量,逗號後有空白,價格已是小數字串(例如 <c>45395.00</c>)。分 K 的時間是那一分鐘的<b>結束</b>、台北時間、日期是日曆日。
/// Parses Capital's domestic historical K-line text (new format). Measured: minute rows "yyyy/MM/dd HH:mm" and daily
/// rows "yyyy/MM/dd", then open, high, low, close and volume separated by ", "; prices are already decimal strings. A
/// minute row's time is the <b>end</b> of the minute, in Taipei time, dated by calendar day.
/// </summary>
/// <remarks>
/// 與海外的 <see cref="CapitalKLineParser"/> 不同:讀不懂的行<b>記數略過</b>,不讓整批失敗;價格以 <c>decimal.Parse</c>(InvariantCulture)
/// 原樣讀入,不四捨五入、不經過 double。
/// Unlike the overseas parser, an unreadable row is <b>counted and skipped</b> rather than failing the batch; prices are
/// read as is with decimal parsing, neither rounded nor passed through double.
/// </remarks>
internal static class CapitalDomesticKLineParser
{
    private const int FieldCount = 6;

    /// <summary>台北時間 UTC+8(台灣 1979 年後沒有夏令時間)。Taipei time, UTC+8; Taiwan has had no daylight saving since 1979.</summary>
    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);

    /// <summary>
    /// 解析整批。
    /// Parses a whole batch.
    /// </summary>
    /// <param name="code">商品代碼。The code.</param>
    /// <param name="type">K 線類型。The bar type.</param>
    /// <param name="lines">原始文字。The raw rows.</param>
    /// <param name="skipped">讀不懂而略過的行數。How many rows could not be read and were skipped.</param>
    /// <returns>讀得懂的 K 線,照原順序。The readable bars, in their original order.</returns>
    public static IReadOnlyList<CapitalDomesticBar> Parse(string code, CapitalDomesticKLineType type, IReadOnlyList<string> lines, out int skipped)
    {
        List<CapitalDomesticBar> bars = new(lines.Count);
        skipped = 0;

        foreach (string line in lines)
        {
            if (TryParseLine(code, type, line, out CapitalDomesticBar? bar))
            {
                bars.Add(bar);
            }
            else
            {
                skipped++;
            }
        }

        return bars;
    }

    /// <summary>
    /// 解析一行。
    /// Parses one row.
    /// </summary>
    /// <param name="code">商品代碼。The code.</param>
    /// <param name="type">K 線類型。The bar type.</param>
    /// <param name="line">原始文字。The raw row.</param>
    /// <param name="bar">一根 K。One bar.</param>
    /// <returns>讀得懂時為 <see langword="true"/>。True when readable.</returns>
    public static bool TryParseLine(string code, CapitalDomesticKLineType type, string? line, [NotNullWhen(true)] out CapitalDomesticBar? bar)
    {
        bar = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string[] fields = line.Split(',', StringSplitOptions.TrimEntries);

        if (fields.Length != FieldCount
            || !TryParsePrice(fields[1], out decimal open)
            || !TryParsePrice(fields[2], out decimal high)
            || !TryParsePrice(fields[3], out decimal low)
            || !TryParsePrice(fields[4], out decimal close)
            || !long.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out long volume))
        {
            return false;
        }

        if (type == CapitalDomesticKLineType.Minute)
        {
            if (!DateTime.TryParseExact(fields[0], "yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime localEnd))
            {
                return false;
            }

            bar = new CapitalDomesticBar
            {
                Code = code,
                Type = type,
                OpenTimeUtc = MinuteBarOpenUtc(localEnd),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume,
            };
            return true;
        }

        if (!DateOnly.TryParseExact(fields[0], "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
        {
            return false;
        }

        bar = new CapitalDomesticBar
        {
            Code = code,
            Type = type,
            TradingDay = day,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
        };
        return true;
    }

    /// <summary>
    /// 分 K 的開始時刻:台北時間的結束時刻 − 1 分鐘,轉 UTC(00:00 那根的開始是前一天 23:59)。
    /// A minute bar's open time: the Taipei end time minus one minute, in UTC (the 00:00 bar opens at 23:59 the day before).
    /// </summary>
    /// <param name="localEnd">群益給的結束時刻(台北時間)。Capital's end time, Taipei.</param>
    /// <returns>開始時刻(UTC)。The open time in UTC.</returns>
    public static DateTimeOffset MinuteBarOpenUtc(DateTime localEnd) =>
        new DateTimeOffset(DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified).AddMinutes(-1), TaipeiOffset).ToUniversalTime();

    private static bool TryParsePrice(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}
