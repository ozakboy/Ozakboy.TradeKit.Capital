namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 國內市場別(群益 sMarketNo,手冊 4-4-17 / 4-4-26;值即群益的數字)。本套件只承諾期貨與選擇權;事件帶來其他數字時照原值轉型保留。
/// The domestic market number (Capital's sMarketNo; the values are Capital's). Only futures and options are supported;
/// any other number in an event is kept as its cast value.
/// </summary>
#pragma warning disable CA1008 // 值即群益的數字,0 不是合法值,不加 None。The values are Capital's numbers and 0 is not valid.
public enum CapitalDomesticMarket
{
    /// <summary>期貨(2)。Futures.</summary>
    Futures = 2,

    /// <summary>選擇權(3)。Options.</summary>
    Options = 3,
}
#pragma warning restore CA1008
