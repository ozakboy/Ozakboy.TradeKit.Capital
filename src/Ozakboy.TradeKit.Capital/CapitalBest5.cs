namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 最佳五檔:國內 <c>OnNotifyBest5LONG</c>(手冊 4-4-n,含衍生一檔與試算旗標)、海外 <c>OnNotifyBest5NineDigitLONG</c>(九位擴充整數,手冊 5-13)、
/// 海外選擇權 <c>OnNotifyBest5LONG</c>。以 <see cref="Market"/> 區分;國內的 <see cref="CapitalMarketEvent.Symbol"/> 交易所是
/// <see cref="CapitalSymbol.DomesticExchange"/>。實測 2026-11-09:買方由高到低、賣方由低到高,沒有掛單的檔位是 0 / 0;國內的衍生一檔
/// 在微台有值(賣方,例如 41200.00 / 1)、台指與小台當時都是 0 / 0。<b>海外與海外選擇權的五檔是由 RequestTicks 還是 RequestMarketDepth 觸發
/// 還分不出來</b>(實測兩個是連著送的,五檔與十檔的則數相同)。
/// The best five levels for the domestic, overseas and overseas option feeds, told apart by Market. Measured: bids
/// high to low, asks low to high, empty levels 0 / 0. Whether ticks or market depth triggers the overseas best 5 is
/// not settled yet.
/// </summary>
public sealed record CapitalBest5 : CapitalMarketEvent
{
    /// <summary>來源。The source.</summary>
    public required CapitalQuoteMarket Market { get; init; }

    /// <summary>小數位數。The decimals.</summary>
    public required int Decimals { get; init; }

    /// <summary>委買,第一檔起(5 檔)。Bids from level 1.</summary>
    public required IReadOnlyList<CapitalBookLevel> Bids { get; init; }

    /// <summary>委賣,第一檔起(5 檔)。Asks from level 1.</summary>
    public required IReadOnlyList<CapitalBookLevel> Asks { get; init; }

    /// <summary>衍生一檔買(只有國內;海外為 null)。The extended bid; domestic only.</summary>
    public CapitalBookLevel? ExtendedBid { get; init; }

    /// <summary>衍生一檔賣(只有國內;海外為 null)。The extended ask; domestic only.</summary>
    public CapitalBookLevel? ExtendedAsk { get; init; }

    /// <summary>是否為試算揭示(只有國內;海外沒有這個欄位為 null)。Whether simulated; domestic only.</summary>
    public bool? IsSimulated { get; init; }

    /// <summary>用戶端收到的 UTC 時間。When the client received it, UTC.</summary>
    public required DateTimeOffset ReceivedUtc { get; init; }
}
