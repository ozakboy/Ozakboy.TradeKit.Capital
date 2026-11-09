using System.Globalization;
using System.Text.RegularExpressions;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>
/// 國內商品清單的一筆(手冊 4-4-h:<c>[商品代碼],[名稱],[最後交易日],[交易所商品代碼],[跳動點],[幣別];</c>)。
/// One entry of the domestic stock list.
/// </summary>
/// <param name="Code">群益商品代碼(訂閱用)。Capital's symbol, used to subscribe.</param>
/// <param name="Name">名稱。Name.</param>
/// <param name="LastTradeDay">最後交易日 yyyyMMdd(0 = 沒有)。Last trading day.</param>
/// <param name="OrderId">交易所商品代碼(例如 TXFI9)。The exchange symbol.</param>
internal sealed record StockListEntry(string Code, string Name, string LastTradeDay, string OrderId);

/// <summary>
/// 探針挑商品的規則。挑選結果與理由都記進 probe.jsonl,挑錯也看得出來。
/// The probe's symbol picking rules. The choice and the reason are both recorded.
/// </summary>
internal static partial class ProbeSymbolPicker
{
    // 期交所選擇權代碼:TXO + 履約價 + 月份字母(A–L 買權、M–X 賣權)+ 年尾數,例如 TXO28500K5;週選是 TX1 / TX2 / TX4 / TX5 …
    // 實測 2026-11-09:群益代碼每檔都有一對,結尾多「AM」的(TXO41500K6AM)只有日盤,沒有 AM 的(TXO41500K6)是全盤(含夜盤);
    // 兩者的交易所代碼相同。夜盤挑到 AM 代碼只會收到日盤的回補,所以優先挑全盤代碼。
    // Measured: every code comes in a pair; the "AM" one is day session only, so the full-session code is preferred.
    private const string CallMonths = "ABCDEFGHIJKL";
    private const string DaySessionSuffix = "AM";

    private static readonly string[] OverseasOptionRoots = ["NQ", "MNQ", "ES", "MES"];
    private static readonly string[] OverseasOptionNames = ["那斯達克", "S&P", "標普", "史坦普"];

    /// <summary>
    /// 解析 <c>OnNotifyStockList</c> 的原文:一則可能含多筆(<c>;</c> 分隔)、多個類股(換行分隔),<c>##</c> 開頭是結尾標記。
    /// Parses raw OnNotifyStockList chunks: entries are separated by ';' and groups by newlines; ## marks the end.
    /// </summary>
    /// <param name="chunks">原文。The raw chunks.</param>
    /// <returns>解析出的商品。The parsed entries.</returns>
    public static IReadOnlyList<StockListEntry> ParseStockList(IEnumerable<string> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        List<StockListEntry> entries = [];

        foreach (string chunk in chunks)
        {
            foreach (string item in chunk.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (item.StartsWith("##", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] fields = item.Split(',');

                if (fields.Length < 2 || fields[0].Trim().Length == 0)
                {
                    continue;
                }

                entries.Add(new StockListEntry(
                    fields[0].Trim(),
                    fields[1].Trim(),
                    fields.Length > 2 ? fields[2].Trim() : string.Empty,
                    fields.Length > 3 ? fields[3].Trim() : string.Empty));
            }
        }

        return entries;
    }

    /// <summary>
    /// 從選擇權清單挑一檔台指選擇權近月、履約價最接近 <paramref name="underlying"/> 的買權:先找月選(TXO),沒有再找週選;
    /// 近月 = 最後交易日不早於今天的最小值;同一檔有全盤代碼時不挑結尾 AM 的日盤代碼(實測 2026-11-09)。
    /// 找不到買權就挑第一檔名稱含「台指 / 臺指」的,再不行就清單第一檔。
    /// Picks the near-month TAIEX call closest to the underlying, preferring the full-session code over the day-only "AM"
    /// code; falls back to the first TAIEX option, then the first entry.
    /// </summary>
    /// <param name="entries">市場 3 的清單。The market-3 list.</param>
    /// <param name="underlying">台指期現價(沒有就挑近月買權中間那檔)。The TX price; null picks the middle strike.</param>
    /// <param name="today">今天 yyyyMMdd。Today.</param>
    /// <param name="reason">挑選理由。Why this one was chosen.</param>
    /// <returns>代碼,清單空的回 null。The code, or null for an empty list.</returns>
    public static StockListEntry? PickTaiexCall(IReadOnlyList<StockListEntry> entries, decimal? underlying, int today, out string reason)
    {
        ArgumentNullException.ThrowIfNull(entries);

        List<(StockListEntry Entry, bool Monthly, decimal Strike, int LastDay)> calls = [];

        foreach (StockListEntry entry in entries)
        {
            Match match = OptionCodePattern().Match(entry.OrderId);

            if (!match.Success)
            {
                match = OptionCodePattern().Match(entry.Code);
            }

            if (!match.Success || !CallMonths.Contains(match.Groups["month"].Value, StringComparison.Ordinal))
            {
                continue;
            }

            decimal strike = decimal.Parse(match.Groups["strike"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
            int lastDay = int.TryParse(entry.LastTradeDay, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
            calls.Add((entry, match.Groups["series"].Value == "O", strike, lastDay));
        }

        if (calls.Exists(call => !IsDaySessionCode(call.Entry.Code)))
        {
            calls.RemoveAll(call => IsDaySessionCode(call.Entry.Code));
        }

        if (calls.Count > 0)
        {
            bool monthly = calls.Exists(call => call.Monthly);
            List<(StockListEntry Entry, bool Monthly, decimal Strike, int LastDay)> series = calls.FindAll(call => call.Monthly == monthly);
            List<int> upcoming = [.. series.Select(call => call.LastDay).Where(day => day >= today).Order()];
            int nearest = upcoming.Count > 0 ? upcoming[0] : series.Max(call => call.LastDay);
            List<(StockListEntry Entry, bool Monthly, decimal Strike, int LastDay)> near = [.. series.Where(call => call.LastDay == nearest).OrderBy(call => call.Strike)];

            (StockListEntry Entry, bool Monthly, decimal Strike, int LastDay) chosen = underlying is decimal price
                ? near.MinBy(call => Math.Abs(call.Strike - price))
                : near[near.Count / 2];

            reason = string.Create(
                CultureInfo.InvariantCulture,
                $"{(monthly ? "月選" : "週選")}買權({(IsDaySessionCode(chosen.Entry.Code) ? "只有日盤代碼" : "全盤代碼")}),最後交易日 {nearest},履約價 {chosen.Strike},{(underlying is null ? "沒有台指期價格,取中間履約價" : $"台指期價格 {underlying}")}");
            return chosen.Entry;
        }

        StockListEntry? taiex = entries.FirstOrDefault(entry => entry.Name.Contains("台指", StringComparison.Ordinal) || entry.Name.Contains("臺指", StringComparison.Ordinal));

        if (taiex is not null)
        {
            reason = "代碼格式解析不出買權,改挑第一檔名稱含台指的選擇權";
            return taiex;
        }

        reason = entries.Count > 0 ? "清單沒有台指選擇權,挑第一檔" : "清單是空的";
        return entries.Count > 0 ? entries[0] : null;
    }

    /// <summary>
    /// 從海外選擇權商品清單挑 <paramref name="count"/> 檔。實測 2026-11-09 的清單格式是 <c>交易所,交易所名稱,代碼,名稱,最後交易日</c>
    /// (名稱結尾「 履約價 C/P」),訂閱字串用「交易所,代碼」。有 <paramref name="underlying"/>(小那斯達克期貨 <c>CME,NQ0000</c> 的價格)時,
    /// 挑 CME 小那斯達克(代碼 <c>NQ</c> + 數字)最後交易日不早於 <paramref name="today"/> 的最近一期、履約價最接近它的買權與賣權——實測照清單順序
    /// 挑到的是履約價 100 的深度價外 / 價內,三分鐘內沒有任何成交。沒有價格或挑不到就照舊:優先 CME 的小那斯達克 / S&amp;P 系列,再來清單前幾檔。
    /// Picks overseas options. With the Nasdaq futures price, the nearest CME NQ expiry's call and put closest to it are
    /// chosen (picking by list order took a 100 strike that never traded); otherwise CME Nasdaq / S&amp;P rows come first.
    /// </summary>
    /// <param name="lines">OnProducts 原文。Raw OnProducts rows.</param>
    /// <param name="count">要幾檔。How many.</param>
    /// <param name="underlying">小那斯達克期貨價格;沒有就 null。The Nasdaq futures price, or null.</param>
    /// <param name="today">今天 yyyyMMdd(0 = 不看最後交易日)。Today, or 0 to ignore the last trading day.</param>
    /// <returns>(訂閱字串, 原文) 清單。Request strings with their raw rows.</returns>
    public static IReadOnlyList<(string Request, string Line)> PickOverseasOptions(IEnumerable<string> lines, int count, decimal? underlying = null, int today = 0)
    {
        ArgumentNullException.ThrowIfNull(lines);

        List<OptionRow> rows = [];

        foreach (string line in lines)
        {
            string text = line.Trim();

            if (text.Length == 0 || text.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = [.. text.Split(',').Select(field => field.Trim())];
            string request = fields.Length >= 3 ? fields[0] + "," + fields[2] : fields[0];
            bool cme = fields.Any(field => field.Equals("CME", StringComparison.OrdinalIgnoreCase));
            bool root = fields.Length >= 3 && OverseasOptionRoots.Any(prefix => fields[2].StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            bool named = OverseasOptionNames.Any(name => text.Contains(name, StringComparison.OrdinalIgnoreCase));
            bool nasdaq = cme && fields.Length >= 3 && NasdaqOptionCodePattern().IsMatch(fields[2]);
            int lastDay = fields.Length >= 5 && int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out int day) ? day : 0;
            rows.Add(new OptionRow(request, text, cme && (root || named), nasdaq, fields.Length >= 4 ? StrikeFromName(fields[3]) : null, lastDay));
        }

        List<(string Request, string Line)> chosen = [];

        if (underlying is decimal price)
        {
            List<OptionRow> nasdaq = [.. rows.Where(row => row.Nasdaq && row.Strike is not null && row.LastDay >= today)];

            if (nasdaq.Count > 0)
            {
                int nearest = nasdaq.Min(row => row.LastDay);
                IEnumerable<OptionRow> closest = nasdaq
                    .Where(row => row.LastDay == nearest)
                    .OrderBy(row => Math.Abs(row.Strike!.Value - price))
                    .ThenBy(row => row.Strike)
                    .ThenBy(row => row.Request, StringComparer.Ordinal);

                foreach (OptionRow row in closest.Take(count))
                {
                    chosen.Add((row.Request, row.Line));
                }

                return chosen;
            }
        }

        foreach (OptionRow row in rows.Where(row => row.Preferred).Concat(rows.Where(row => !row.Preferred)))
        {
            if (chosen.Count >= count)
            {
                break;
            }

            if (!chosen.Exists(item => item.Request == row.Request))
            {
                chosen.Add((row.Request, row.Line));
            }
        }

        return chosen;
    }

    /// <summary>結尾 AM 的群益代碼只有日盤(實測)。A code ending in AM is day session only, as measured.</summary>
    /// <param name="code">群益代碼。Capital's code.</param>
    /// <returns>是日盤代碼時為 true。True for a day-only code.</returns>
    public static bool IsDaySessionCode(string code) =>
        code.Length > DaySessionSuffix.Length && code.EndsWith(DaySessionSuffix, StringComparison.Ordinal);

    /// <summary>
    /// 從海外選擇權名稱的結尾「… 履約價 C/P」讀履約價(實測例:「小那季選2612P 27200 P」→ 27200);讀不到為 null。
    /// Reads the strike from the "… strike C/P" end of an overseas option name; null when absent.
    /// </summary>
    /// <param name="name">商品名稱。The product name.</param>
    /// <returns>履約價或 null。The strike or null.</returns>
    public static decimal? StrikeFromName(string name)
    {
        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[^1] is "C" or "P"
            && decimal.TryParse(parts[^2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal strike)
            ? strike
            : null;
    }

    [GeneratedRegex(@"^TX(?<series>[A-Z0-9])(?<strike>\d{4,5})(?<month>[A-X])(?<year>\d)$", RegexOptions.CultureInvariant)]
    private static partial Regex OptionCodePattern();

    // 實測:小那斯達克季選 NQ + 5 位履約價 + 月份字母 + 年尾數(NQ27200L6);MNQ 等其他根代碼不算。NQ quarterly options, as measured.
    [GeneratedRegex(@"^NQ\d+[A-X]\d$", RegexOptions.CultureInvariant)]
    private static partial Regex NasdaqOptionCodePattern();

    /// <summary>海外選擇權清單的一列(挑選用)。One overseas option row, for picking.</summary>
    private readonly record struct OptionRow(string Request, string Line, bool Preferred, bool Nasdaq, decimal? Strike, int LastDay);
}
