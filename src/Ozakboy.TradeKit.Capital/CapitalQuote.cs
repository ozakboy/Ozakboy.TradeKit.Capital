namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆報價(<c>OnNotifyQuoteLONG</c> → <c>GetStockByIndexNineDigitLONG</c>)。價格已由原始整數 ÷ 10^<see cref="Decimals"/>
/// 換成 decimal。
/// One quote. Prices are already converted from raw integers divided by 10^<see cref="Decimals"/>.
/// </summary>
/// <remarks>
/// 實測(2026-11-06):外匯的 <see cref="TotalQuantity"/> 是報價次數,不是成交量;外匯的 <see cref="BidQuantity"/> 與
/// <see cref="AskQuantity"/> 是 0。
/// Measured: for FX, <see cref="TotalQuantity"/> counts quote updates rather than traded volume, and the bid and ask
/// quantities are 0.
/// </remarks>
public sealed record CapitalQuote : CapitalMarketEvent
{
    /// <summary>商品名稱(群益的中文名)。The product name, as Capital gives it.</summary>
    public required string Name { get; init; }

    /// <summary>價格小數位數(<c>sDecimal</c>)。The price's decimal places.</summary>
    public required int Decimals { get; init; }

    /// <summary>交易日(<c>nTradingDay</c>,群益依交易所時段切日)。The trading day, cut by the exchange's session.</summary>
    public required DateOnly TradingDay { get; init; }

    /// <summary>開盤價。Open.</summary>
    public required decimal Open { get; init; }

    /// <summary>最高價。High.</summary>
    public required decimal High { get; init; }

    /// <summary>最低價。Low.</summary>
    public required decimal Low { get; init; }

    /// <summary>成交價(最新)。The latest price.</summary>
    public required decimal Close { get; init; }

    /// <summary>結算價。Settlement price.</summary>
    public required decimal Settle { get; init; }

    /// <summary>參考價(<c>nRef</c>)。Reference price.</summary>
    public required decimal Reference { get; init; }

    /// <summary>買價。Bid.</summary>
    public required decimal Bid { get; init; }

    /// <summary>買量。Bid quantity.</summary>
    public required long BidQuantity { get; init; }

    /// <summary>賣價。Ask.</summary>
    public required decimal Ask { get; init; }

    /// <summary>賣量。Ask quantity.</summary>
    public required long AskQuantity { get; init; }

    /// <summary>總量(<c>nTQty</c>;外匯是報價次數)。Total quantity; a quote count for FX.</summary>
    public required long TotalQuantity { get; init; }

    /// <summary>用戶端收到這筆報價的 UTC 時間。When the client received this quote, UTC.</summary>
    public required DateTimeOffset ReceivedUtc { get; init; }
}
