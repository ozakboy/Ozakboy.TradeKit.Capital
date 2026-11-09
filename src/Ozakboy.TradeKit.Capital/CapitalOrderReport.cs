namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆委託回報(<c>GetOrderReport</c>,手冊 4-2-97、5-4-4;阻塞式查詢、直接回字串,宿主強制兩次間隔 5.5 秒)。具名屬性只在格式 1–6、9
/// (同一套 70 欄,手冊編號 1 起算)時依手冊解析(<b>待實測</b>);格式 7、8 欄位不同,只有 <see cref="RawFields"/>。代碼類欄位(委託狀態、
/// 盤別、委託條件…)照原文,意義見手冊 5-4-4。<see cref="ToString"/> 只露帳號末 3 碼。
/// One order report row. Named properties are parsed only for formats 1-6 and 9 (<b>not measured yet</b>); formats 7 and 8
/// differ and only carry RawFields. Code fields stay verbatim. ToString shows only the last 3 characters of the account.
/// </summary>
public sealed record CapitalOrderReport
{
    /// <summary>請求的種類。The requested kind.</summary>
    public required CapitalOrderReportFormat Format { get; init; }

    /// <summary>1 市場別(TW / TS / TF / OS / OF)。The market.</summary>
    public string? Market { get; init; }

    /// <summary>2 商品別(STO / FUT / OPT…)。The product kind.</summary>
    public string? ProductKind { get; init; }

    /// <summary>3 交易所別(TAIFEX…)。The exchange.</summary>
    public string? Exchange { get; init; }

    /// <summary>4 分公司代號。The branch code.</summary>
    public string? BrokerId { get; init; }

    /// <summary>6 交易帳號。The account.</summary>
    public string? AccountNo { get; init; }

    /// <summary>8 委託書號。The order book number.</summary>
    public string? BookNo { get; init; }

    /// <summary>9 13 碼電子流水號。The 13-digit sequence number.</summary>
    public string? SequenceNo { get; init; }

    /// <summary>11 委託狀態原文(0 預約、2 全部成交、3 全部取消…、F1–F4 動態退單)。The order status code, verbatim.</summary>
    public string? Status { get; init; }

    /// <summary>12 委託日期。The order date.</summary>
    public DateOnly? OrderDate { get; init; }

    /// <summary>13 委託時間(hhmmss)。The order time.</summary>
    public TimeOnly? OrderTime { get; init; }

    /// <summary>14 交易歸屬日。The trade date.</summary>
    public DateOnly? TradeDate { get; init; }

    /// <summary>16 商品代號。The product code.</summary>
    public string? Symbol { get; init; }

    /// <summary>23 買賣別(B / S)。Buy or sell.</summary>
    public string? Side { get; init; }

    /// <summary>26 委託條件原文(0 ROD、3 IOC、4 FOK…)。The time-in-force code, verbatim.</summary>
    public string? TimeInForce { get; init; }

    /// <summary>27 委託方式原文(1 市價、2 限價、3 範圍市價…)。The price-type code, verbatim.</summary>
    public string? PriceType { get; init; }

    /// <summary>28 委託價。The order price.</summary>
    public decimal? Price { get; init; }

    /// <summary>30 有效委託數量。The active quantity.</summary>
    public long? ValidQuantity { get; init; }

    /// <summary>31 原始委託數量。The original quantity.</summary>
    public long? OriginalQuantity { get; init; }

    /// <summary>32 成交數量。The filled quantity.</summary>
    public long? FilledQuantity { get; init; }

    /// <summary>33 剩餘數量。The remaining quantity.</summary>
    public long? RemainingQuantity { get; init; }

    /// <summary>34 當沖註記原文(Y / N / O / A)。The day-trade flag, verbatim.</summary>
    public string? DayTradeFlag { get; init; }

    /// <summary>35 判別 T+1 盤原文(A T 盤、B T+1 盤)。The session flag, verbatim.</summary>
    public string? SessionFlag { get; init; }

    /// <summary>36 是否錯誤回報(Y → true、N → false,其他 null)。Whether it is an error report.</summary>
    public bool? IsErrorReport { get; init; }

    /// <summary>41 Call / Put(只有選擇權)。Call or put; options only.</summary>
    public string? CallPut { get; init; }

    /// <summary>50 交易所回覆訊息。The exchange's message.</summary>
    public string? ExchangeMessage { get; init; }

    /// <summary>原始欄位(手冊編號 n 在索引 n − 1)。The raw fields; manual number n is at index n - 1.</summary>
    public required IReadOnlyList<string> RawFields { get; init; }

    /// <summary>不含帳號全碼的描述。A description without the full account number.</summary>
    /// <returns>描述。The description.</returns>
    public override string ToString() =>
        $"{nameof(CapitalOrderReport)} {{ Format = {Format}, BookNo = {BookNo}, Symbol = {Symbol}, Side = {Side}, Status = {Status}, Price = {Price}, Account = {CapitalAccountText.Mask(AccountNo)} }}";
}
