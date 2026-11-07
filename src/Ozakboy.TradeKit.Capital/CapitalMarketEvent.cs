namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// <see cref="CapitalHostSession.Events"/> 送出的行情事件:<see cref="CapitalQuote"/> 或 <see cref="CapitalTick"/>。
/// A market event from <see cref="CapitalHostSession.Events"/>: a <see cref="CapitalQuote"/> or a <see cref="CapitalTick"/>.
/// </summary>
public abstract record CapitalMarketEvent
{
    /// <summary>商品。The symbol.</summary>
    public required CapitalSymbol Symbol { get; init; }
}
