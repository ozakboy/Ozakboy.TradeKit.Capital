namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆海外選擇權報價(SKOOQuoteLib:<c>OnNotifyQuoteLONG</c> → <c>GetStockByIndexLONG</c> 的 <c>SKFOREIGNLONG</c>)。價格 = 原始整數 ÷
/// 10^<see cref="Decimals"/>。V2.13.59 手冊沒有 SKOOQuoteLib 的章節。
/// <para>
/// <b>實測 2026-11-09</b>(CME 小 S&amp;P 季選 <c>ES00100X6</c> / <c>ES00100L6</c>,兩檔當天都沒有成交):<c>sDecimal</c> = 2,
/// 結算價 730200 → 7302.00(深度價內買權)、賣權賣價 5 → 0.05,與 ES 選擇權 0.05 的跳動點一致;履約價 <c>nStrikePrice</c> = 100 與商品名稱
/// 「小SP季選2612C 100 C」一致,<b>沒有</b>乘上 10^<see cref="Decimals"/>;但非整數履約價的商品未實測(商品清單裡十年債 <c>TY12350N7</c>
/// 的名稱寫 12350,推定是 123.50 放大過),所以 <see cref="StrikePriceRaw"/> 照原值給、不換算。
/// 沒有成交時開高低收是 0、沒有買價時買價是 0:價格欄位的 0 一律給 <see langword="null"/>。成交時間的時區未實測(當時沒有成交)。
/// </para>
/// One overseas option quote. Measured on 2026-11-09 with two CME E-mini S&amp;P options: sDecimal 2 applies to the
/// prices, while nStrikePrice is the strike as it is (100, matching the product name), not scaled. Zero prices mean
/// "no value" (no trade, no bid) and are null.
/// </summary>
public sealed record CapitalOverseasOptionQuote : CapitalMarketEvent
{
    /// <summary>商品名稱(實測中文正常,例如「小SP季選2612C」)。The name.</summary>
    public required string Name { get; init; }

    /// <summary>交易所名稱。The exchange name.</summary>
    public required string ExchangeName { get; init; }

    /// <summary>買權 / 賣權(<c>bstrCallPut</c> 原文;實測 <c>C</c> / <c>P</c>)。Call or put, verbatim (C or P as measured).</summary>
    public required string CallPut { get; init; }

    /// <summary>小數位數。The decimals.</summary>
    public required int Decimals { get; init; }

    /// <summary>交易日;讀不懂時為 null。The trading day; null when unreadable.</summary>
    public DateOnly? TradingDay { get; init; }

    /// <summary>開盤價;0 → null。Open; null for zero.</summary>
    public decimal? Open { get; init; }

    /// <summary>最高價;0 → null。High; null for zero.</summary>
    public decimal? High { get; init; }

    /// <summary>最低價;0 → null。Low; null for zero.</summary>
    public decimal? Low { get; init; }

    /// <summary>成交價;0(當天沒有成交)→ null。The latest price; null for zero.</summary>
    public decimal? Close { get; init; }

    /// <summary>結算價;0 → null。Settlement price; null for zero.</summary>
    public decimal? Settle { get; init; }

    /// <summary>參考價;0 → null。Reference price; null for zero.</summary>
    public decimal? Reference { get; init; }

    /// <summary>買價;0(沒有買價)→ null。Bid; null for zero.</summary>
    public decimal? Bid { get; init; }

    /// <summary>買量。Bid quantity.</summary>
    public required long BidQuantity { get; init; }

    /// <summary>賣價;0(沒有賣價)→ null。Ask; null for zero.</summary>
    public decimal? Ask { get; init; }

    /// <summary>賣量。Ask quantity.</summary>
    public required long AskQuantity { get; init; }

    /// <summary>總量。Total volume.</summary>
    public required long TotalQuantity { get; init; }

    /// <summary>
    /// 履約價原值(<c>nStrikePrice</c>;實測 ES 沒有乘上 10^<see cref="Decimals"/>,100 就是 100;非整數履約價的商品未實測,不換算)。
    /// The raw strike; measured unscaled for ES (100 for a 100 strike); products with fractional strikes are unmeasured.
    /// </summary>
    public required long StrikePriceRaw { get; init; }

    /// <summary>用戶端收到的 UTC 時間。When the client received it, UTC.</summary>
    public required DateTimeOffset ReceivedUtc { get; init; }
}
