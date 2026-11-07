namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆海外商品(商品清單併商品明細)。
/// One overseas product: the product list merged with the product details.
/// </summary>
public sealed record CapitalProduct
{
    /// <summary>商品。The symbol.</summary>
    public required CapitalSymbol Symbol { get; init; }

    /// <summary>交易所名稱(群益的中文名,例如「國際外匯」)。The exchange name as Capital gives it.</summary>
    public required string ExchangeName { get; init; }

    /// <summary>商品名稱。The product name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// 商品種類(明細的最後一欄;實測 1 指數、2 外匯、5 期貨、6–8 價差、10 選擇權);沒有明細時為 <see langword="null"/>。
    /// The product kind (measured: 1 index, 2 FX, 5 futures, 6-8 spreads, 10 options); null without details.
    /// </summary>
    public int? Kind { get; init; }

    /// <summary>最後交易日;群益送 <c>99991231</c>(外匯、指數等不到期的商品)時為 <see langword="null"/>。The last trading day; null for 99991231.</summary>
    public DateOnly? LastTradeDate { get; init; }

    /// <summary>第一通知日;群益送 <c>0</c> 時為 <see langword="null"/>。The first notice day; null for 0.</summary>
    public DateOnly? FirstNoticeDate { get; init; }

    /// <summary>下單交易所(明細;外匯實測為 <c>--</c>,照原樣保留)。The order exchange; "--" for FX, kept verbatim.</summary>
    public string? OrderExchange { get; init; }

    /// <summary>下單代碼(明細,例如 <c>NQ_202612</c>;外匯為 <c>--</c>)。The order code, for example NQ_202612.</summary>
    public string? OrderCode { get; init; }
}
