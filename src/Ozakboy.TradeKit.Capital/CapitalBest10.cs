namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 最佳十檔(市場深度):海外 <c>RequestMarketDepth</c> → <c>OnNotifyBest10NineDigitLONG</c>、海外選擇權 → <c>OnNotifyBest10LONG</c>。
/// 實測 2026-11-09(CME 小那斯達克、CME 小 S&amp;P 選擇權):買方由高到低、賣方由低到高各 10 檔,前 5 檔與同時的五檔相同,價格 ÷ 10^小數位數
/// (NQ 2721775 → 27217.75);沒有掛單的檔位是 0 / 0(外匯 SUSDTWD 整組都是 0)。
/// The best ten levels of the overseas and overseas option feeds, measured: bids high to low, asks low to high, the
/// first five equal the best 5, and empty levels are 0 / 0.
/// </summary>
public sealed record CapitalBest10 : CapitalMarketEvent
{
    /// <summary>來源(<see cref="CapitalQuoteMarket.Overseas"/> 或 <see cref="CapitalQuoteMarket.OverseasOptions"/>)。The source.</summary>
    public required CapitalQuoteMarket Market { get; init; }

    /// <summary>小數位數。The decimals.</summary>
    public required int Decimals { get; init; }

    /// <summary>委買,第一檔起(10 檔)。Bids from level 1.</summary>
    public required IReadOnlyList<CapitalBookLevel> Bids { get; init; }

    /// <summary>委賣,第一檔起(10 檔)。Asks from level 1.</summary>
    public required IReadOnlyList<CapitalBookLevel> Asks { get; init; }

    /// <summary>用戶端收到的 UTC 時間。When the client received it, UTC.</summary>
    public required DateTimeOffset ReceivedUtc { get; init; }
}
