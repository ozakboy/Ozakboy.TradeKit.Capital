namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆海外選擇權商品(SKOOQuoteLib 的 <c>RequestProducts</c> → <c>OnProducts</c>,手冊沒有章節)。
/// <para>
/// <b>實測 2026-11-09</b>:56,156 筆全部是 5 欄 <c>交易所,交易所名稱,代碼,名稱,最後交易日</c>,例如
/// <c>CME,芝加哥商品交易所,ES00100X6,小SP季選2612P 100 P,20261218</c>,以 <c>##,##,##,##,##</c> 結尾(宿主不回這一行)。
/// 交易所有 CME、CBOT、NYM、Eurex、HKEx、ICEEU、OSE(後四個不在 <see cref="CapitalExchangeClock"/> 的已實測時區)。
/// 代碼 = 根代碼 + 履約價(補零的數字)+ 月份字母(<c>A</c>–<c>L</c> 買權 1–12 月、<c>M</c>–<c>X</c> 賣權 1–12 月)+ 年尾數,例如 <c>ES00100X6</c>
/// = 小 S&amp;P 2026 年 12 月 100 賣權;根代碼長短不一、可能含數字或底線(<c>E2A</c>、<c>2HS</c>、<c>P_I</c>),所以本套件不拆代碼。
/// 名稱結尾是「 履約價 C/P」;名稱與代碼裡的履約價可能是放大過的(十年債 <c>TY12350N7</c> 的名稱寫 12350,推定是 123.50)。
/// 訂閱字串是 <c>交易所,代碼</c>(<see cref="Symbol"/>),實測以 <c>#</c> 串接送出成功。
/// </para>
/// One overseas option product. Measured on 2026-11-09: every row has five fields
/// (exchange, exchange name, code, name, last trading day) and the list ends with a "##" row. Codes are root + padded
/// strike + month letter (A-L calls, M-X puts) + year digit; roots vary, so the code is not split here.
/// </summary>
public sealed record CapitalOverseasOptionProduct
{
    /// <summary>交易所代碼(第 1 欄,例如 <c>CME</c>)。The exchange code.</summary>
    public string? Exchange { get; init; }

    /// <summary>交易所名稱(第 2 欄)。The exchange name.</summary>
    public string? ExchangeName { get; init; }

    /// <summary>商品代碼(第 3 欄,例如 <c>ES00100X6</c>)。The product code.</summary>
    public string? Code { get; init; }

    /// <summary>商品名稱(第 4 欄,例如「小SP季選2612P 100 P」)。The product name.</summary>
    public string? Name { get; init; }

    /// <summary>最後交易日(第 5 欄 yyyyMMdd;讀不懂為 null)。The last trading day; null when unreadable.</summary>
    public DateOnly? LastTradeDate { get; init; }

    /// <summary>
    /// 訂閱用的「交易所,代碼」;兩欄都讀得到且合法時才有,否則為 null。
    /// The "exchange,code" to subscribe with; null unless both fields are present and valid.
    /// </summary>
    public CapitalSymbol? Symbol { get; init; }

    /// <summary>原始欄位(以逗號切開、去掉前後空白)。The raw fields, split on commas and trimmed.</summary>
    public required IReadOnlyList<string> RawFields { get; init; }
}
