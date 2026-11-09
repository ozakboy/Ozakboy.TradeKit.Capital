namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 海外期貨權益數一筆(<c>GetRequestOverSeaFutureRight</c> → <c>OnOverSeaFutureRight</c>,手冊 4-2-45、4-2-k;第一筆是查詢結果,
/// 成功「000,筆數,訊息」、失敗「代碼;訊息」,之後每個幣別一筆)。具名屬性依手冊 4-2-k 的 31 欄順序(0 起算)解析,讀不懂就是 null。
/// <b>實測 2026-11-09</b>(沒有部位的帳戶):第一筆 <c>000,1,查詢成功</c>、資料列 31 欄(第 1 欄 IB 代號是 <c>000</c>、
/// 第 22 欄參考匯率 = 1,與手冊順序對得上;0.3.0 的早期草稿漏了 IB 代號與「未沖銷期貨浮動損益」兩欄,已改正)、以 <c>##</c> 結尾。
/// 沒有部位時風險指標與足額維持率是空字串(→ null)。金額欄當時全是 0,<b>數值語意待有部位時再驗</b>。<see cref="ToString"/> 只露帳號末 3 碼。
/// One overseas futures rights row. The named properties follow the manual's 31 fields. Measured on 2026-11-09 with an
/// account without positions: the first row is <c>000,1,&lt;message&gt;</c>, data rows have 31 fields (the reference rate
/// at index 22 reads 1, matching the manual), and the list ends with <c>##</c>. The amounts were all zero, so their
/// meaning still needs an account with positions. ToString shows only the last 3 characters of the account.
/// </summary>
public sealed record CapitalOverseaFutureRights
{
    /// <summary>0 期貨商代碼(實測 <c>F020000</c>)。The futures broker code.</summary>
    public string? BrokerId { get; init; }

    /// <summary>1 IB 代號(實測 <c>000</c>)。The IB code.</summary>
    public string? IbCode { get; init; }

    /// <summary>2 帳號。The account.</summary>
    public string? AccountNo { get; init; }

    /// <summary>
    /// 3 幣別;群益的中文會亂碼,已經過 <see cref="CapitalSkcomText.Repair"/>(實測基幣那筆是 <c>NTD(基幣)</c>)。
    /// Currency, passed through <see cref="CapitalSkcomText.Repair"/>.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>4 風險指標 / 維持率(沒有部位時是空字串 → null)。Risk indicator; null without positions.</summary>
    public decimal? RiskIndicator { get; init; }

    /// <summary>5 足額維持率(沒有部位時是空字串 → null)。Full maintenance ratio; null without positions.</summary>
    public decimal? FullMaintenanceRatio { get; init; }

    /// <summary>6 可動用(出金)保證金。Available margin.</summary>
    public decimal? AvailableMargin { get; init; }

    /// <summary>7 委託保證金。Order margin.</summary>
    public decimal? OrderMargin { get; init; }

    /// <summary>8 維持保證金。Maintenance margin.</summary>
    public decimal? MaintenanceMargin { get; init; }

    /// <summary>9 原始保證金。Initial margin.</summary>
    public decimal? InitialMargin { get; init; }

    /// <summary>10 淨值權益數 / 權益總值。Net equity.</summary>
    public decimal? NetEquity { get; init; }

    /// <summary>11 預扣權利金。Reserved premium.</summary>
    public decimal? ReservedPremium { get; init; }

    /// <summary>12 未沖銷買方選擇權市值。Long option market value.</summary>
    public decimal? LongOptionValue { get; init; }

    /// <summary>13 未沖銷賣方選擇權市值。Short option market value.</summary>
    public decimal? ShortOptionValue { get; init; }

    /// <summary>14 帳戶權益 / 權益數。Equity.</summary>
    public decimal? Equity { get; init; }

    /// <summary>15 盤中未實現。Intraday unrealized profit and loss.</summary>
    public decimal? UnrealizedProfitLoss { get; init; }

    /// <summary>16 未沖銷期貨浮動損益。Floating profit and loss of open futures.</summary>
    public decimal? FloatingProfitLoss { get; init; }

    /// <summary>17 今日權利金支付。Premium paid today.</summary>
    public decimal? PremiumToday { get; init; }

    /// <summary>18 本日期貨平倉損益淨額。Futures realized profit and loss today.</summary>
    public decimal? RealizedProfitLossToday { get; init; }

    /// <summary>19 已實現費用 / 手續費。Fees.</summary>
    public decimal? Fees { get; init; }

    /// <summary>20 今日存提款。Deposits and withdrawals today.</summary>
    public decimal? DepositWithdrawalToday { get; init; }

    /// <summary>21 前日帳戶餘額。Previous balance.</summary>
    public decimal? PreviousBalance { get; init; }

    /// <summary>22 參考匯率(實測台幣那筆是 1)。Reference exchange rate; 1 for NTD as measured.</summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>23 選擇權到期差益。Option expiry gain.</summary>
    public decimal? OptionExpiryGain { get; init; }

    /// <summary>24 選擇權到期差損。Option expiry loss.</summary>
    public decimal? OptionExpiryLoss { get; init; }

    /// <summary>25 期貨交割損益。Futures delivery profit and loss.</summary>
    public decimal? FuturesDeliveryProfitLoss { get; init; }

    /// <summary>26 超額 / 追繳保證金。Excess or call margin.</summary>
    public decimal? ExcessMargin { get; init; }

    /// <summary>27 到期履約損益。Expiry exercise profit and loss.</summary>
    public decimal? ExerciseProfitLoss { get; init; }

    /// <summary>28 帳戶餘額。Account balance.</summary>
    public decimal? AccountBalance { get; init; }

    /// <summary>29 固定保證金。Fixed margin.</summary>
    public decimal? FixedMargin { get; init; }

    /// <summary>30 變動保證金。Variable margin.</summary>
    public decimal? VariableMargin { get; init; }

    /// <summary>原始欄位。The raw fields.</summary>
    public required IReadOnlyList<string> RawFields { get; init; }

    /// <summary>不含帳號全碼的描述。A description without the full account number.</summary>
    /// <returns>描述。The description.</returns>
    public override string ToString() =>
        $"{nameof(CapitalOverseaFutureRights)} {{ Currency = {Currency}, Equity = {Equity}, AvailableMargin = {AvailableMargin}, Account = {CapitalAccountText.Mask(AccountNo)} }}";
}
