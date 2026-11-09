namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 國內期貨權益數一筆(<c>GetFutureRights</c> → <c>OnFutureRights</c>,手冊 4-2-41、4-2-i;每個幣別一筆,全幣別時第一筆是基幣)。
/// 具名屬性依手冊 4-2-i 的欄位順序(0 起算)以 <c>decimal.Parse</c>(InvariantCulture)讀,讀不懂就是 null(不填 0);
/// 第 21 欄(手冊又寫一次「原始保證金」)與第 39 欄 LOGIN_ID 不給具名屬性,在 <see cref="RawFields"/>。<see cref="ToString"/> 只露帳號末 3 碼。
/// <b>實測 2026-11-09</b>(全幣別、沒有部位的帳戶):41 欄與欄位位置對得上(第 39 欄是登入帳號、第 40 欄是分公司代碼 + 帳號),第一筆幣別欄是
/// <c>NTD(基幣)</c>、第二筆是 <c>NTD</c>;沒有部位時維持率 / 風險指標是 <c>*********</c>(→ null)。金額欄當時全是 0,<b>數值語意待有部位時再驗</b>。
/// One domestic futures rights row, one per currency. The named properties follow the manual's order and are null
/// when unreadable, never zero. Field 21 (a second "initial margin" in the manual) and field 39 (the login ID) have no
/// named property. ToString shows only the last 3 characters of the account number. Measured on 2026-11-09 with an
/// account without positions: the 41 positions match and the first row is the base currency (<c>NTD(基幣)</c>); the
/// ratios read <c>*********</c> (null) without positions. The amounts were all zero, so their meaning still needs an
/// account with positions.
/// </summary>
public sealed record CapitalFutureRights
{
    /// <summary>0 帳戶餘額。Account balance.</summary>
    public decimal? AccountBalance { get; init; }

    /// <summary>1 浮動損益。Floating profit and loss.</summary>
    public decimal? FloatingProfitLoss { get; init; }

    /// <summary>2 已實現費用。Realized fees.</summary>
    public decimal? RealizedFees { get; init; }

    /// <summary>3 交易稅。Transaction tax.</summary>
    public decimal? TransactionTax { get; init; }

    /// <summary>4 預扣權利金。Reserved premium.</summary>
    public decimal? ReservedPremium { get; init; }

    /// <summary>5 權利金收付。Premium received or paid.</summary>
    public decimal? PremiumReceivedPaid { get; init; }

    /// <summary>6 權益數。Equity.</summary>
    public decimal? Equity { get; init; }

    /// <summary>7 超額保證金。Excess margin.</summary>
    public decimal? ExcessMargin { get; init; }

    /// <summary>8 存提款。Deposits and withdrawals.</summary>
    public decimal? DepositWithdrawal { get; init; }

    /// <summary>9 買方市值。Long option market value.</summary>
    public decimal? LongMarketValue { get; init; }

    /// <summary>10 賣方市值。Short option market value.</summary>
    public decimal? ShortMarketValue { get; init; }

    /// <summary>11 期貨平倉損益。Futures realized profit and loss.</summary>
    public decimal? FuturesRealizedProfitLoss { get; init; }

    /// <summary>12 盤中未實現。Intraday unrealized profit and loss.</summary>
    public decimal? UnrealizedProfitLoss { get; init; }

    /// <summary>13 原始保證金。Initial margin.</summary>
    public decimal? InitialMargin { get; init; }

    /// <summary>14 維持保證金。Maintenance margin.</summary>
    public decimal? MaintenanceMargin { get; init; }

    /// <summary>15 部位原始保證金。Position initial margin.</summary>
    public decimal? PositionInitialMargin { get; init; }

    /// <summary>16 部位維持保證金。Position maintenance margin.</summary>
    public decimal? PositionMaintenanceMargin { get; init; }

    /// <summary>17 委託保證金。Order margin.</summary>
    public decimal? OrderMargin { get; init; }

    /// <summary>18 超額最佳保證金。Excess optimal margin.</summary>
    public decimal? ExcessOptimalMargin { get; init; }

    /// <summary>19 權利總值。Total option value.</summary>
    public decimal? TotalOptionValue { get; init; }

    /// <summary>20 預扣費用。Reserved fees.</summary>
    public decimal? ReservedFees { get; init; }

    /// <summary>22 昨日餘額。Previous balance.</summary>
    public decimal? PreviousBalance { get; init; }

    /// <summary>23 選擇權組合單加不加收保證金(原文)。The option combination margin flag, verbatim.</summary>
    public string? OptionCombinationMarginFlag { get; init; }

    /// <summary>24 維持率。Maintenance ratio.</summary>
    public decimal? MaintenanceRatio { get; init; }

    /// <summary>
    /// 25 幣別;群益的中文會亂碼,已經過 <see cref="CapitalSkcomText.Repair"/>(實測基幣那筆是 <c>NTD(基幣)</c>)。
    /// Currency, passed through <see cref="CapitalSkcomText.Repair"/>; the base-currency row reads <c>NTD(基幣)</c>.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>26 足額原始保證金。Full initial margin.</summary>
    public decimal? FullInitialMargin { get; init; }

    /// <summary>27 足額維持保證金。Full maintenance margin.</summary>
    public decimal? FullMaintenanceMargin { get; init; }

    /// <summary>28 足額可用。Full available.</summary>
    public decimal? FullAvailable { get; init; }

    /// <summary>29 抵繳金額。Collateral amount.</summary>
    public decimal? CollateralAmount { get; init; }

    /// <summary>30 有價可用。Securities available.</summary>
    public decimal? SecuritiesAvailable { get; init; }

    /// <summary>31 可用餘額。Available balance.</summary>
    public decimal? AvailableBalance { get; init; }

    /// <summary>32 足額現金可用。Full cash available.</summary>
    public decimal? FullCashAvailable { get; init; }

    /// <summary>33 有價價值。Securities value.</summary>
    public decimal? SecuritiesValue { get; init; }

    /// <summary>34 風險指標。Risk indicator.</summary>
    public decimal? RiskIndicator { get; init; }

    /// <summary>35 選擇權到期差異。Option expiry difference.</summary>
    public decimal? OptionExpiryDifference { get; init; }

    /// <summary>36 選擇權到期差損。Option expiry loss.</summary>
    public decimal? OptionExpiryLoss { get; init; }

    /// <summary>37 期貨到期損益。Futures expiry profit and loss.</summary>
    public decimal? FuturesExpiryProfitLoss { get; init; }

    /// <summary>38 加收保證金。Additional margin.</summary>
    public decimal? AdditionalMargin { get; init; }

    /// <summary>40 帳號。The account.</summary>
    public string? AccountNo { get; init; }

    /// <summary>原始欄位。The raw fields.</summary>
    public required IReadOnlyList<string> RawFields { get; init; }

    /// <summary>不含帳號全碼的描述。A description without the full account number.</summary>
    /// <returns>描述。The description.</returns>
    public override string ToString() =>
        $"{nameof(CapitalFutureRights)} {{ Currency = {Currency}, Equity = {Equity}, AvailableBalance = {AvailableBalance}, Account = {CapitalAccountText.Mask(AccountNo)} }}";
}
