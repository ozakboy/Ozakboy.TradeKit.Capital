namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 國內權益數的幣別(<c>GetFutureRights</c> 的 sCoinType,手冊 4-2-41;值即群益的數字)。
/// The currency selection of the domestic rights query (sCoinType; the values are Capital's).
/// </summary>
public enum CapitalCoinType
{
    /// <summary>全幣別(含基幣;第一筆是基幣)。Every currency; the first row is the base currency.</summary>
    All = 0,

    /// <summary>基幣(台幣)。The base currency, TWD.</summary>
    BaseCurrency = 1,

    /// <summary>人民幣。RMB.</summary>
    Rmb = 2,
}
