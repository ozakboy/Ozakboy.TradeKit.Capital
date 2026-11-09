namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 委託回報查詢的種類(<c>GetOrderReport</c> 的 nFormat,手冊 4-2-97;值即群益的數字)。
/// The order report kind (nFormat; the values are Capital's).
/// </summary>
#pragma warning disable CA1008 // 值即群益的數字,0 不是合法值,不加 None。The values are Capital's numbers and 0 is not valid.
public enum CapitalOrderReportFormat
{
    /// <summary>全部。All.</summary>
    All = 1,

    /// <summary>有效。Active.</summary>
    Active = 2,

    /// <summary>可消。Cancellable.</summary>
    Cancellable = 3,

    /// <summary>已消。Cancelled.</summary>
    Cancelled = 4,

    /// <summary>已成。Filled.</summary>
    Filled = 5,

    /// <summary>失敗。Failed.</summary>
    Failed = 6,

    /// <summary>合併同價格(欄位不同,只有 RawFields)。Merged by price; raw fields only.</summary>
    MergedByPrice = 7,

    /// <summary>合併同商品(欄位不同,只有 RawFields)。Merged by product; raw fields only.</summary>
    MergedByProduct = 8,

    /// <summary>預約。Scheduled.</summary>
    Scheduled = 9,
}
#pragma warning restore CA1008
