namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆成交回報(<c>GetFulfillReport</c>,手冊 4-2-98、5-4-5;阻塞式查詢,宿主強制兩次間隔 5.5 秒)。具名屬性只在格式 1、5(同一套 52 欄,
/// 手冊編號 1 起算)時依手冊解析(<b>待實測</b>);格式 2–4 欄位不同,只有 <see cref="RawFields"/>。<see cref="ToString"/> 只露帳號末 3 碼。
/// One fill report row. Named properties are parsed only for formats 1 and 5 (<b>not measured yet</b>); formats 2-4
/// differ and only carry RawFields. ToString shows only the last 3 characters of the account.
/// </summary>
public sealed record CapitalFulfillReport
{
    /// <summary>請求的種類。The requested kind.</summary>
    public required CapitalFulfillReportFormat Format { get; init; }

    /// <summary>1 市場別。The market.</summary>
    public string? Market { get; init; }

    /// <summary>2 商品別。The product kind.</summary>
    public string? ProductKind { get; init; }

    /// <summary>3 交易所別。The exchange.</summary>
    public string? Exchange { get; init; }

    /// <summary>4 分公司代號。The branch code.</summary>
    public string? BrokerId { get; init; }

    /// <summary>6 交易帳號。The account.</summary>
    public string? AccountNo { get; init; }

    /// <summary>8 委託書號。The order book number.</summary>
    public string? BookNo { get; init; }

    /// <summary>9 成交序號。The fill sequence number.</summary>
    public string? FillSequenceNo { get; init; }

    /// <summary>10 成交日期。The fill date.</summary>
    public DateOnly? FillDate { get; init; }

    /// <summary>11 成交時間(hhmmss)。The fill time.</summary>
    public TimeOnly? FillTime { get; init; }

    /// <summary>12 交易歸屬日。The trade date.</summary>
    public DateOnly? TradeDate { get; init; }

    /// <summary>13 商品代號。The product code.</summary>
    public string? Symbol { get; init; }

    /// <summary>14 商品名稱。The product name.</summary>
    public string? Name { get; init; }

    /// <summary>23 買賣別。Buy or sell.</summary>
    public string? Side { get; init; }

    /// <summary>27 成交價。The fill price.</summary>
    public decimal? Price { get; init; }

    /// <summary>28 成交量。The fill quantity.</summary>
    public long? Quantity { get; init; }

    /// <summary>29 當沖註記原文。The day-trade flag, verbatim.</summary>
    public string? DayTradeFlag { get; init; }

    /// <summary>30 判別 T+1 盤原文。The session flag, verbatim.</summary>
    public string? SessionFlag { get; init; }

    /// <summary>38 委託日期。The order date.</summary>
    public DateOnly? OrderDate { get; init; }

    /// <summary>39 委託時間。The order time.</summary>
    public TimeOnly? OrderTime { get; init; }

    /// <summary>原始欄位(手冊編號 n 在索引 n − 1)。The raw fields; manual number n is at index n - 1.</summary>
    public required IReadOnlyList<string> RawFields { get; init; }

    /// <summary>不含帳號全碼的描述。A description without the full account number.</summary>
    /// <returns>描述。The description.</returns>
    public override string ToString() =>
        $"{nameof(CapitalFulfillReport)} {{ Format = {Format}, BookNo = {BookNo}, Symbol = {Symbol}, Side = {Side}, Price = {Price}, Quantity = {Quantity}, Account = {CapitalAccountText.Mask(AccountNo)} }}";
}
