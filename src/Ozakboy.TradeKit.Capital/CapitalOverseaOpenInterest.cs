namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 海外期貨 / 選擇權未平倉一筆(<c>GetOverseaFutureOpenInterestGW</c> → <c>OnOFOpenInterestGWReport</c>,手冊 4-2-76、4-2-q;第一筆
/// 「@@,代碼,訊息」是查詢狀態,不是資料)。彙總與明細的欄位不同,只在該格式有的屬性才有值。<b>欄位位置待實測。</b>
/// 價格含分子分母(手冊例「379 02/4」)時 decimal 屬性為 null、原文在 <see cref="PreviousSettlementText"/> 與 <see cref="RawFields"/>。
/// <see cref="ToString"/> 只露帳號末 3 碼。
/// One overseas open-interest row. Summary and detail rows differ, so only the properties of the row's format are set.
/// <b>The field positions are not measured yet.</b> A fractional price leaves the decimal null. ToString shows only the
/// last 3 characters of the account.
/// </summary>
public sealed record CapitalOverseaOpenInterest
{
    /// <summary>請求的格式。The requested format.</summary>
    public required CapitalOverseaOpenInterestFormat Format { get; init; }

    /// <summary>帳號。The account.</summary>
    public string? AccountNo { get; init; }

    /// <summary>成交日期(只有明細)。The trade date; detail only.</summary>
    public DateOnly? TradeDate { get; init; }

    /// <summary>交易所代號。The exchange code.</summary>
    public string? ExchangeCode { get; init; }

    /// <summary>交易所中文名稱。The exchange name.</summary>
    public string? ExchangeName { get; init; }

    /// <summary>不含年月的商品代碼(例如 <c>NQ</c>)。The product code without the month.</summary>
    public string? Product { get; init; }

    /// <summary>含年月的商品代號(只有彙總,例如 <c>NG     202012</c>)。The product with its month; summary only.</summary>
    public string? ProductWithMonth { get; init; }

    /// <summary>商品年月原文。The contract month, verbatim.</summary>
    public string? YearMonth { get; init; }

    /// <summary>商品中文名稱。The product name.</summary>
    public string? Name { get; init; }

    /// <summary>買賣別原文。Buy or sell, verbatim.</summary>
    public string? Side { get; init; }

    /// <summary>未平倉口數。Open contracts.</summary>
    public long? Quantity { get; init; }

    /// <summary>成交口數(只有明細)。Filled contracts; detail only.</summary>
    public long? FilledQuantity { get; init; }

    /// <summary>平均價(只有彙總)。Average price; summary only.</summary>
    public decimal? AveragePrice { get; init; }

    /// <summary>成交價(只有明細)。Trade price; detail only.</summary>
    public decimal? TradePrice { get; init; }

    /// <summary>現價(非即時)。Market price, not real time.</summary>
    public decimal? MarketPrice { get; init; }

    /// <summary>未平損益。Unrealized profit and loss.</summary>
    public decimal? UnrealizedProfitLoss { get; init; }

    /// <summary>昨日結算價(只有彙總;含分數時為 null)。Previous settlement; summary only, null when fractional.</summary>
    public decimal? PreviousSettlement { get; init; }

    /// <summary>昨日結算價原文(只有彙總)。Previous settlement, verbatim; summary only.</summary>
    public string? PreviousSettlementText { get; init; }

    /// <summary>當沖未平口數(只有彙總)。Day-trade open contracts; summary only.</summary>
    public long? DayTradeQuantity { get; init; }

    /// <summary>當沖註記原文(只有明細)。The day-trade flag, verbatim; detail only.</summary>
    public string? DayTradeFlag { get; init; }

    /// <summary>委託書序號(只有明細)。The order book number; detail only.</summary>
    public string? BookNo { get; init; }

    /// <summary>原始保證金。Initial margin.</summary>
    public decimal? InitialMargin { get; init; }

    /// <summary>是否為選擇權(0 期貨、1 選擇權)。Whether it is an option.</summary>
    public bool? IsOption { get; init; }

    /// <summary>履約價(只有選擇權)。The strike; options only.</summary>
    public decimal? StrikePrice { get; init; }

    /// <summary>C / P(只有選擇權彙總)。Call or put; option summaries only.</summary>
    public string? CallPut { get; init; }

    /// <summary>原始欄位。The raw fields.</summary>
    public required IReadOnlyList<string> RawFields { get; init; }

    /// <summary>不含帳號全碼的描述。A description without the full account number.</summary>
    /// <returns>描述。The description.</returns>
    public override string ToString() =>
        $"{nameof(CapitalOverseaOpenInterest)} {{ Format = {Format}, ExchangeCode = {ExchangeCode}, Product = {Product}, YearMonth = {YearMonth}, Side = {Side}, Quantity = {Quantity}, Account = {CapitalAccountText.Mask(AccountNo)} }}";
}
