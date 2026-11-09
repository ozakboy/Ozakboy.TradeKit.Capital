namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 成交回報查詢的種類(<c>GetFulfillReport</c> 的 nFormat,手冊 4-2-98;值即群益的數字)。
/// The fill report kind (nFormat; the values are Capital's).
/// </summary>
#pragma warning disable CA1008 // 值即群益的數字,0 不是合法值,不加 None。The values are Capital's numbers and 0 is not valid.
public enum CapitalFulfillReportFormat
{
    /// <summary>完整。Full.</summary>
    Full = 1,

    /// <summary>合併同書號(欄位不同,只有 RawFields)。Merged by order number; raw fields only.</summary>
    MergedByBookNo = 2,

    /// <summary>合併同價格(欄位不同,只有 RawFields)。Merged by price; raw fields only.</summary>
    MergedByPrice = 3,

    /// <summary>合併同商品(欄位不同,只有 RawFields)。Merged by product; raw fields only.</summary>
    MergedByProduct = 4,

    /// <summary>T+1 成交回報(欄位同完整)。The T+1 session fills, laid out like Full.</summary>
    NextSession = 5,
}
#pragma warning restore CA1008
