using System.Globalization;

namespace FakeCapitalHostExe;

/// <summary>
/// 假宿主的國內期貨歷史 K 線,行為照 2026-11-07 <c>probe-tw</c> 實測(SKCOM 2.13.59 x64,只有期貨戶的帳號):
/// <list type="bullet">
/// <item>可用代碼 <c>TX00</c> / <c>MTX00</c> / <c>TM0000</c>;其他代碼回 9999(SK_FAIL,期貨戶查 2330 實測),<c>…AM</c> 結尾回 3023
/// (SK_SUBJECT_STOCKNO_IS_INVALID,<c>TX00AM</c> 實測)。</item>
/// <item>起訖日期是交易日;全盤(session 0)含前一個交易日 15:01–05:00 的夜盤(查週一從上週五 15:01 開始),AM 盤(1)只有 08:46–13:45。</item>
/// <item>分 K 格式 <c>yyyy/MM/dd HH:mm, O, H, L, C, V</c>,時間是那一分鐘的<b>結束</b>、台北時間、日期是日曆日(00:00 那根是新的一天);
/// 日 K <c>yyyy/MM/dd, O, H, L, C, V</c>;價格是 <c>45395.00</c> 這種小數字串。</item>
/// <item>沒有成交的分鐘照樣給量為 0 的空 K 棒。</item>
/// <item>一個請求最多 60,000 筆,超過保留最新、截掉最舊。</item>
/// </list>
/// 交易日在這裡簡化成週一到週五(不含國定假日);價格是決定性的假走勢,不是實測值。
/// The fake host's domestic futures K-lines, following the 2026-11-07 probe-tw measurements: the usable codes, 9999 and
/// 3023 for unknown codes, trading-day dates with the previous night session in the full session, end-of-minute Taipei
/// times dated by calendar day, zero-volume empty bars, and the 60,000-row cap keeping the newest. Trading days are
/// simplified to Monday to Friday and the prices are a deterministic fake walk, not measured values.
/// </summary>
internal static class FakeDomesticMarket
{
    /// <summary>SK_FAIL:代碼不存在或沒有權限(期貨戶查股票 2330 實測)。Unknown code or no permission.</summary>
    public const int CodeFail = 9999;

    /// <summary>SK_SUBJECT_STOCKNO_IS_INVALID(<c>TX00AM</c> 實測)。The code is invalid, as measured for TX00AM.</summary>
    public const int CodeInvalidStockNo = 3023;

    private const int NightBars = 840; // 15:01 … 05:00
    private const int DayBars = 300;   // 08:46 … 13:45

    private static readonly Dictionary<string, decimal> BasePrices = new(StringComparer.Ordinal)
    {
        ["TX00"] = 39786.00m,
        ["MTX00"] = 39786.00m,
        ["TM0000"] = 39786.00m,
    };

    /// <summary>RequestKLineAMByDate 的回傳代碼。The code RequestKLineAMByDate answers for this code.</summary>
    public static int Check(string code)
    {
        if (BasePrices.ContainsKey(code))
        {
            return 0;
        }

        return code.EndsWith("AM", StringComparison.Ordinal) ? CodeInvalidStockNo : CodeFail;
    }

    /// <summary>
    /// 這次請求群益會送的全部行(已套用上限)。All rows Capital would send for this request, with the cap applied.
    /// </summary>
    public static List<string> Rows(string code, short type, short session, string start, string end, int limit)
    {
        DateOnly first = DateOnly.ParseExact(start, "yyyyMMdd", CultureInfo.InvariantCulture);
        DateOnly last = DateOnly.ParseExact(end, "yyyyMMdd", CultureInfo.InvariantCulture);
        decimal basePrice = BasePrices[code];
        List<string> rows = [];
        int step = 0;
        decimal close = basePrice;

        for (DateOnly day = first; day <= last; day = day.AddDays(1))
        {
            if (!IsTradingDay(day))
            {
                continue;
            }

            if (type == 4)
            {
                decimal open = close;
                close = Walk(basePrice, step++);
                rows.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{day:yyyy/MM/dd}, {Price(open)}, {Price(Math.Max(open, close) + 15m)}, {Price(Math.Min(open, close) - 15m)}, {Price(close)}, {100_000 + step}"));
                continue;
            }

            if (session == 0)
            {
                // 夜盤歸下一個交易日:前一個交易日 15:00 之後的 840 根。The night session belongs to the next trading day.
                DateTime nightStart = PreviousTradingDay(day).ToDateTime(new TimeOnly(15, 0));

                for (int minute = 1; minute <= NightBars; minute++)
                {
                    rows.Add(MinuteRow(nightStart.AddMinutes(minute), basePrice, ref step, ref close));
                }
            }

            DateTime dayStart = day.ToDateTime(new TimeOnly(8, 45));

            for (int minute = 1; minute <= DayBars; minute++)
            {
                rows.Add(MinuteRow(dayStart.AddMinutes(minute), basePrice, ref step, ref close));
            }
        }

        // 超過上限:群益保留最新、截掉最舊(實測查 8/1–11/7 只回 8/18 21:16 起的 60,000 筆)。Beyond the cap the oldest go.
        if (rows.Count > limit)
        {
            rows.RemoveRange(0, rows.Count - limit);
        }

        return rows;
    }

    private static string MinuteRow(DateTime end, decimal basePrice, ref int step, ref decimal close)
    {
        int index = step++;
        decimal open = close;

        // 每 13 根有一根沒有成交:量 0、開高低收都是前一根的收盤。One bar in 13 has no trade: zero volume, OHLC at the last close.
        if (index % 13 == 5)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{end:yyyy/MM/dd HH:mm}, {Price(open)}, {Price(open)}, {Price(open)}, {Price(open)}, 0");
        }

        close = Walk(basePrice, index);
        decimal high = Math.Max(open, close) + 2m;
        decimal low = Math.Min(open, close) - 2m;
        return string.Create(CultureInfo.InvariantCulture, $"{end:yyyy/MM/dd HH:mm}, {Price(open)}, {Price(high)}, {Price(low)}, {Price(close)}, {1 + (index % 50)}");
    }

    private static decimal Walk(decimal basePrice, int index) => basePrice + ((index * 7 % 41) - 20);

    private static string Price(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static bool IsTradingDay(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    private static DateOnly PreviousTradingDay(DateOnly day)
    {
        DateOnly previous = day.AddDays(-1);

        while (!IsTradingDay(previous))
        {
            previous = previous.AddDays(-1);
        }

        return previous;
    }
}
