namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆國內商品(<c>SKQuoteLib_RequestStockList</c> → <c>OnNotifyStockList</c>,手冊 4-4-h:
/// <c>[商品代碼],[名稱],[最後交易日],[交易所商品代碼],[跳動點],[幣別]</c>)。具名屬性依固定欄位位置解析,解析不出就是
/// <see langword="null"/>(不填 0、不猜),原始欄位一律在 <see cref="RawFields"/>。
/// <para>
/// <b>實測 2026-11-09</b>:期貨(市場 2)6,341 筆、選擇權(市場 3)13,424 筆都是 6 欄,例如
/// <c>TX00,台指近,20261118,TXFK600,100000| 1 | ,NTD</c>、<c>TXO41500K6,台選11C,20261118,TXO41500K6,10| 0.1 | / 50| 0.5 | / …,NTD</c>。
/// <list type="bullet">
/// <item><b>AM 代碼</b>:每個商品都有一對代碼,結尾多 <c>AM</c> 的(<c>TX00AM</c>、<c>TXO41500K6AM</c>,期貨 532 對、選擇權 3,724 對)
/// 只有一般交易時段(日盤);沒有 <c>AM</c> 的是全盤(含夜盤)。實測夜盤訂 <c>TXO41500K6AM</c> 只收到日盤的回補(最後一筆 13:30:39)、
/// 沒有夜盤成交,報價的交易日是當天;要盤後(夜盤)行情請訂沒有 <c>AM</c> 的代碼。兩者的 <see cref="ExchangeCode"/> 相同。</item>
/// <item><b>選擇權代碼</b>:<c>TXO</c> + 履約價 + 月份字母(<c>A</c>–<c>L</c> 買權 1–12 月、<c>M</c>–<c>X</c> 賣權 1–12 月)+ 年尾數,
/// 例如 <c>TXO41500K6</c> = 2026 年 11 月 41500 買權;週選與其他標的的根代碼不同(<c>TX2</c>、<c>TXV</c>、<c>TXX</c>、<c>TEO</c>…)。</item>
/// <item><b>系列列</b>:選擇權清單另有 120 筆 5 欄的列(例如 <c>TXO11,台選11,0,TXO11,</c>),最後交易日 0、沒有跳動點與幣別,推定是系列分類而不是合約。</item>
/// <item>價差商品(例如 <c>TX11/12AM</c>、交易所代碼 <c>TXFK6/L6</c>)也在清單裡。</item>
/// </list>
/// </para>
/// One domestic product from the stock list, measured on 2026-11-09. Every code comes in a pair: the one ending in
/// "AM" covers only the regular (day) session, the plain one the full session including the night session. Option codes
/// are TXO + strike + month letter (A-L calls, M-X puts) + year digit. Option series rows have five fields only.
/// </summary>
public sealed record CapitalDomesticProduct
{
    /// <summary>市場別(請求時指定的那個)。The market that was requested.</summary>
    public required CapitalDomesticMarket Market { get; init; }

    /// <summary>
    /// 群益商品代碼(訂閱用,例如 <c>TX00</c>;結尾 <c>AM</c> 的只有日盤)。Capital's code, used to subscribe; an "AM" suffix means day session only.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>中文名稱(實測正常,例如「台指近」「台選11C」)。The Chinese name.</summary>
    public string? Name { get; init; }

    /// <summary>最後交易日;群益送 <c>0</c>(沒有或不可交易)或讀不懂時為 <see langword="null"/>。The last trading day; null for 0.</summary>
    public DateOnly? LastTradeDate { get; init; }

    /// <summary>交易所商品代碼(例如 <c>TXFK6</c>;近月連續代碼實測是 <c>TXFK600</c>)。The exchange's code.</summary>
    public string? ExchangeCode { get; init; }

    /// <summary>
    /// 跳動點原文(實測格式「價格上限| 跳動點 |」以 <c>/</c> 分段,例如 <c>100000| 1 | </c>、<c>10| 0.1 | / 50| 0.5 | / …</c>,照原文保留)。
    /// The tick-size text, kept verbatim ("upper bound| tick |" segments separated by '/').
    /// </summary>
    public string? TickSize { get; init; }

    /// <summary>幣別(例如 <c>NTD</c>;系列列沒有)。The currency; series rows have none.</summary>
    public string? Currency { get; init; }

    /// <summary>原始欄位(以逗號切開、去掉前後空白)。The raw fields, split on commas and trimmed.</summary>
    public required IReadOnlyList<string> RawFields { get; init; }
}
