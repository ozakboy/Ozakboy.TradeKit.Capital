using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 帳務查詢(SKOrderLib)回傳中文的亂碼辨識與部分還原。
/// <para>
/// <b>實測 2026-11-09</b>(SKCOM 2.13.59 x64、Windows 系統 ANSI 字碼頁 950):SKOrderLib 事件字串裡的中文是 Big5 位元組被一個位元組當一個字元
/// (Latin-1)放進字串,再經字碼頁 950 的「最適對應」(best fit)來回轉一次的結果,例如「查無資料」→ <c>?dμL﹐eRA</c>、「查詢成功」→
/// <c>?d﹐s|‥￥\</c>、「NTD(基幣)」→ <c>NTD(°o1o)</c>(三筆都以本類別的轉換重現到每個字元)。最適對應會把不同位元組併成同一個字
/// (<c>è é ê ë ð</c> 都變 <c>e</c>,沒有對應的變 <c>?</c>),所以<b>一般無法還原</b>:<see cref="Repair"/> 只把實測看過、可以確定的固定字樣換回中文,
/// 其餘原樣保留;<see cref="LooksGarbled"/> 判斷文字是否疑似這種亂碼。報價端(SKQuoteLib、SKOSQuoteLib、SKOOQuoteLib)的中文實測正常,不需要這個。
/// 在系統字碼頁不是 950 的電腦上亂碼的樣子會不同(未實測),那時 <see cref="Repair"/> 認不得就原樣回傳。
/// </para>
/// Detects and partly repairs garbled Chinese in the account (SKOrderLib) replies. Measured on 2026-11-09 with SKCOM
/// 2.13.59 x64 on Windows with ANSI code page 950: SKOrderLib widens each Big5 byte into one character and then sends
/// the text through a best-fit round trip of code page 950, so "查無資料" arrives as <c>?dμL﹐eRA</c>. Best fit merges
/// different bytes into the same character, so the text cannot be restored in general: <see cref="Repair"/> only
/// restores fixed phrases seen in the measurement and leaves everything else as it is, and <see cref="LooksGarbled"/>
/// tells whether a text looks garbled this way. The quote libraries' Chinese was measured correct.
/// </summary>
public static class CapitalSkcomText
{
    /// <summary>
    /// 字碼頁 950 最適對應下,位元組 0x80–0xFF 當成 Latin-1 字元來回轉一次的結果(以 Windows <c>WideCharToMultiByte</c> /
    /// <c>MultiByteToWideChar</c> 實測 2026-11-09 產生;索引 = 位元組 − 0x80)。
    /// The best-fit images of the bytes 0x80-0xFF read as Latin-1 through code page 950, measured with the Windows API.
    /// </summary>
    /// <remarks>
    /// 以字碼寫,避免全形 / 半形相似字(<c>￥</c> 與 <c>¥</c>、<c>μ</c> 與 <c>µ</c>)在編輯時被換掉。Written as code points so look-alike characters cannot be swapped.
    /// </remarks>
    private static readonly ushort[] BestFitImages =
    [
        0x0080, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F,
        0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F,
        0x003F, 0x0021, 0xFFE0, 0xFFE1, 0x003F, 0xFFE5, 0x007C, 0x00A7, 0x2025, 0x0063, 0x0061, 0x003F, 0x003F, 0x002D, 0x0052, 0x00AF,
        0x00B0, 0x00B1, 0x0032, 0x0033, 0x2019, 0x03BC, 0x003F, 0x00B7, 0xFE50, 0x0031, 0x006F, 0x003F, 0x003F, 0x003F, 0x003F, 0x003F,
        0x0041, 0x0041, 0x0041, 0x0041, 0x0041, 0x0041, 0x0041, 0x0043, 0x0045, 0x0045, 0x0045, 0x0045, 0x0049, 0x0049, 0x0049, 0x0049,
        0x0044, 0x004E, 0x004F, 0x004F, 0x004F, 0x004F, 0x004F, 0x00D7, 0x004F, 0x0055, 0x0055, 0x0055, 0x0055, 0x0059, 0x0054, 0x0073,
        0x0061, 0x0061, 0x0061, 0x0061, 0x0061, 0x0061, 0x0061, 0x0063, 0x0065, 0x0065, 0x0065, 0x0065, 0x0069, 0x0069, 0x0069, 0x0069,
        0x0065, 0x006E, 0x006F, 0x006F, 0x006F, 0x006F, 0x006F, 0x00F7, 0x006F, 0x0075, 0x0075, 0x0075, 0x0075, 0x0079, 0x0074, 0x0079,
    ];

    /// <summary>
    /// 能確定還原的固定字樣(實測 2026-11-09 看過的;「基幣」由手冊 4-2-i「全幣別回傳第一筆資料為基幣」對上第一筆的幣別欄)。
    /// The fixed phrases that can be restored for sure, all seen in the measurement.
    /// </summary>
    private static readonly string[] KnownPhrases = ["查無資料", "查詢成功", "基幣"];

    /// <summary>亂碼裡才會出現的非 ASCII 字元(上表的非 ASCII 結果)。The non-ASCII characters only the garbling produces.</summary>
    private static readonly SearchValues<char> GarbledMarks =
        SearchValues.Create([.. BestFitImages.Where(code => code >= 0x80).Distinct().Select(code => (char)code)]);

    /// <summary>(亂碼, 中文),長的先換。(garbled, Chinese) pairs, longest first.</summary>
    private static readonly (string Garbled, string Text)[] Repairs =
        [.. KnownPhrases.Select(phrase => (Garble(phrase), phrase)).OrderByDescending(pair => pair.Item1.Length)];

    /// <summary>
    /// 把實測看過的固定亂碼字樣換回中文(例如 <c>?dμL﹐eRA</c> → 查無資料、<c>NTD(°o1o)</c> → NTD(基幣)),其他文字原樣回傳;
    /// 正常的中文不受影響。
    /// Replaces the measured garbled phrases with their Chinese text and leaves everything else unchanged; correct Chinese
    /// text is not affected.
    /// </summary>
    /// <param name="text">SKOrderLib 回傳的文字。Text from SKOrderLib.</param>
    /// <returns>還原後的文字;<paramref name="text"/> 是 null 時回 null。The repaired text, or null for null.</returns>
    [return: NotNullIfNotNull(nameof(text))]
    public static string? Repair(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string result = text;

        foreach ((string garbled, string phrase) in Repairs)
        {
            result = result.Replace(garbled, phrase, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// 文字是否疑似 SKOrderLib 的中文亂碼:含有亂碼才會出現的字元(<c>μ ﹐ ‥ ￥ ° ±</c> 等)或 <c>?</c>。只是啟發式判斷:
    /// 回 <see langword="false"/> 不保證文字正確(有些中文字的亂碼全是英數字)。
    /// Whether the text looks like SKOrderLib's garbled Chinese: it contains a character only the garbling produces, or a
    /// question mark. A heuristic only; false does not prove the text is correct.
    /// </summary>
    /// <param name="text">文字。The text.</param>
    /// <returns>疑似亂碼時為 <see langword="true"/>。True when it looks garbled.</returns>
    public static bool LooksGarbled(string? text) =>
        !string.IsNullOrEmpty(text) && (text.AsSpan().ContainsAny(GarbledMarks) || text.Contains('?', StringComparison.Ordinal));

    /// <summary>
    /// 重現 SKOrderLib 的亂碼:Big5(字碼頁 950)位元組逐一當 Latin-1 字元,再套字碼頁 950 最適對應。只給 <see cref="KnownPhrases"/> 與測試用。
    /// Reproduces SKOrderLib's garbling: Big5 bytes widened one by one, then the code page 950 best-fit images.
    /// </summary>
    /// <param name="text">中文。The Chinese text.</param>
    /// <returns>亂碼。The garbled text.</returns>
    internal static string Garble(string text)
    {
        // 不呼叫 Encoding.RegisterProvider(會改到整個行程),直接向提供者拿字碼頁 950;遇到 Big5 沒有的字直接丟例外,不靜默換成 ?。
        // The provider is asked directly instead of registered process-wide; a character Big5 lacks throws.
        Encoding big5 = CodePagesEncodingProvider.Instance.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            ?? throw new InvalidOperationException("字碼頁 950 不可用。Code page 950 is unavailable.");
        byte[] bytes = big5.GetBytes(text);
        StringBuilder builder = new(bytes.Length);

        foreach (byte value in bytes)
        {
            _ = builder.Append(value < 0x80 ? (char)value : (char)BestFitImages[value - 0x80]);
        }

        return builder.ToString();
    }
}
