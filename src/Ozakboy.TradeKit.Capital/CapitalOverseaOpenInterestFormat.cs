namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 海外期貨未平倉的查詢格式(<c>GetOverseaFutureOpenInterestGW</c> 的 nFormat,手冊 4-2-76;值即群益的數字)。
/// The overseas open-interest format (nFormat; the values are Capital's).
/// </summary>
#pragma warning disable CA1008 // 值即群益的數字,0 不是合法值,不加 None。The values are Capital's numbers and 0 is not valid.
public enum CapitalOverseaOpenInterestFormat
{
    /// <summary>彙總。Summary.</summary>
    Summary = 1,

    /// <summary>明細。Detail.</summary>
    Detail = 2,
}
#pragma warning restore CA1008
