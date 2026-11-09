namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 國內期貨未平倉一筆(<c>GetOpenInterestGW</c> → <c>OnOpenInterest</c>,手冊 4-2-89、4-2-d「(新)期貨未平倉 GW 格式 1」)。
/// 具名屬性只在 <see cref="Format"/> = 1 時依手冊欄位解析(<b>待實測</b>),其他格式只有 <see cref="RawFields"/>;查無資料
/// (<c>001,查無資料,帳號</c>)時回空清單。第 10 欄 LOGIN_ID 不給具名屬性。<see cref="ToString"/> 只露帳號末 3 碼。
/// One domestic open-interest row. Named properties are parsed only for format 1 (<b>not measured yet</b>); other formats
/// only carry RawFields. "No data" yields an empty list. ToString shows only the last 3 characters of the account.
/// </summary>
public sealed record CapitalOpenInterest
{
    /// <summary>請求的格式。The requested format.</summary>
    public required int Format { get; init; }

    /// <summary>市場別。The market.</summary>
    public string? Market { get; init; }

    /// <summary>帳號。The account.</summary>
    public string? AccountNo { get; init; }

    /// <summary>商品。The product.</summary>
    public string? Product { get; init; }

    /// <summary>買賣別原文(B / S)。Buy or sell, verbatim.</summary>
    public string? Side { get; init; }

    /// <summary>未平倉部位。Open position.</summary>
    public long? Quantity { get; init; }

    /// <summary>當沖未平倉部位。Day-trade open position.</summary>
    public long? DayTradeQuantity { get; init; }

    /// <summary>平均成本(手冊:小數部分已處理)。Average cost.</summary>
    public decimal? AverageCost { get; init; }

    /// <summary>單口手續費。Fee per contract.</summary>
    public decimal? FeePerContract { get; init; }

    /// <summary>交易稅(萬分之 X 的 X)。The tax rate in basis points.</summary>
    public decimal? TaxRate { get; init; }

    /// <summary>商品下單代碼(手冊:查詢異常時是空值)。The order code; empty when the lookup failed.</summary>
    public string? OrderCode { get; init; }

    /// <summary>原始欄位。The raw fields.</summary>
    public required IReadOnlyList<string> RawFields { get; init; }

    /// <summary>不含帳號全碼的描述。A description without the full account number.</summary>
    /// <returns>描述。The description.</returns>
    public override string ToString() =>
        $"{nameof(CapitalOpenInterest)} {{ Product = {Product}, Side = {Side}, Quantity = {Quantity}, AverageCost = {AverageCost}, Account = {CapitalAccountText.Mask(AccountNo)} }}";
}
