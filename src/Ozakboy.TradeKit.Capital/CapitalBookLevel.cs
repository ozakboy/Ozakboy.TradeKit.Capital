namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一檔委買或委賣:價格(÷ 10^小數位數)與量。群益送 0 / 0 的空檔照原樣保留(實測 2026-11-09:沒有掛單的檔位就是 0 / 0;
/// 市價委託是否另有表示法未實測)。價格 0 不是真的價格,使用前請先排除。
/// One bid or ask level: price and quantity. Empty 0 / 0 levels (measured) are kept as Capital sends them; a zero price
/// is not a price.
/// </summary>
/// <param name="Price">價格。The price.</param>
/// <param name="Quantity">量。The quantity.</param>
public readonly record struct CapitalBookLevel(decimal Price, long Quantity);
