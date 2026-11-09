namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一筆國內期貨 / 選擇權報價(<c>OnNotifyQuoteLONG</c> → <c>GetStockByIndexLONG</c> 的 <c>SKSTOCKLONG</c>,手冊 4-4-k、5-21)。
/// 價格 = 原始整數 ÷ 10^<see cref="Decimals"/>(實測台指期 <c>sDecimal</c> = 2:4120100 → 41201.00)。<see cref="CapitalMarketEvent.Symbol"/>
/// 的交易所固定是 <see cref="CapitalSymbol.DomesticExchange"/>。
/// <para>
/// <b>0 → null(實測 2026-11-09)</b>:每則事件是當下的完整快照,但訂閱後的第一則可能只填了一部分(例如小台第一則只有開高低、成交價與買賣價都是 0;
/// 台指第一則只有成交價與買賣價、開高低是 0),約 10 毫秒內下一則就補齊,之後不再出現 0。國內期貨 / 選擇權的價格不可能是 0,所以價格欄位的 0 一律
/// 當成「這則沒有值」給 <see langword="null"/>(沒有買價 / 賣價、還沒開盤也是 null),不沿用上一則(要合併由呼叫端決定)。數量欄位照原值(0 可能是真的 0,
/// 也可能是第一則還沒填),只有第一則可能如此。
/// </para>
/// One domestic futures or options quote. Prices are raw integers divided by 10^Decimals (TX: sDecimal 2, measured).
/// Measured on 2026-11-09: every event is a full snapshot, yet the first one after subscribing may be only partly
/// filled, with zeros the next event fills about 10 ms later. A domestic price cannot be zero, so a zero price is null
/// ("no value in this snapshot"; also no bid / ask or not opened yet) and is not carried over from an earlier event.
/// Quantities stay as they are.
/// </summary>
public sealed record CapitalDomesticQuote : CapitalMarketEvent
{
    /// <summary>
    /// 群益商品代碼(訂閱時給的那個;實測群益回的 <c>bstrStockNo</c> 也就是它,例如 <c>TX00</c>,不是交易所合約代碼)。
    /// Capital's code, as subscribed; Capital echoes the same code, not the exchange contract.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>市場別。The market.</summary>
    public required CapitalDomesticMarket Market { get; init; }

    /// <summary>商品名稱(實測中文正常,例如「台指近」)。The name.</summary>
    public required string Name { get; init; }

    /// <summary>小數位數(<c>sDecimal</c>;實測台指期、台指選擇權都是 2)。The decimals; 2 for TX and TXO as measured.</summary>
    public required int Decimals { get; init; }

    /// <summary>開盤價;0 → null。Open; null for zero.</summary>
    public decimal? Open { get; init; }

    /// <summary>最高價;0 → null。High; null for zero.</summary>
    public decimal? High { get; init; }

    /// <summary>最低價;0 → null。Low; null for zero.</summary>
    public decimal? Low { get; init; }

    /// <summary>成交價(最新);0 → null。The latest price; null for zero.</summary>
    public decimal? Close { get; init; }

    /// <summary>參考價 / 昨收(<c>nRef</c>);0 → null。The reference price; null for zero.</summary>
    public decimal? Reference { get; init; }

    /// <summary>買價;0(沒有買價)→ null。Bid; null for zero (no bid).</summary>
    public decimal? Bid { get; init; }

    /// <summary>買量。Bid quantity.</summary>
    public required long BidQuantity { get; init; }

    /// <summary>賣價;0(沒有賣價)→ null。Ask; null for zero (no ask).</summary>
    public decimal? Ask { get; init; }

    /// <summary>賣量。Ask quantity.</summary>
    public required long AskQuantity { get; init; }

    /// <summary>單量(<c>nTickQty</c>)。The last tick quantity.</summary>
    public required long TickQuantity { get; init; }

    /// <summary>總量(<c>nTQty</c>)。The total volume.</summary>
    public required long TotalQuantity { get; init; }

    /// <summary>昨量(<c>nYQty</c>)。Yesterday's volume.</summary>
    public required long YesterdayQuantity { get; init; }

    /// <summary>外盤量(買盤,<c>nTBc</c>)。Volume traded at the ask.</summary>
    public required long BuyVolume { get; init; }

    /// <summary>內盤量(賣盤,<c>nTAc</c>)。Volume traded at the bid.</summary>
    public required long SellVolume { get; init; }

    /// <summary>期貨未平倉量(<c>nFutureOI</c>)。Futures open interest.</summary>
    public required long OpenInterest { get; init; }

    /// <summary>漲停價;0 → null。The limit-up price; null for zero.</summary>
    public decimal? LimitUp { get; init; }

    /// <summary>跌停價;0 → null。The limit-down price; null for zero.</summary>
    public decimal? LimitDown { get; init; }

    /// <summary>是否為試算揭示(<c>nSimulate</c> = 1)。Whether this is a simulated display.</summary>
    public required bool IsSimulated { get; init; }

    /// <summary>
    /// 交易日(<c>nTradingDay</c>;讀不懂時為 null)。實測夜盤(11/09 20:07)的全盤代碼是下一個交易日 20261110;只有日盤的 AM 代碼是當天 20261109。
    /// The trading day; a night-session quote carries the next trading day as measured, while a day-only AM code carries
    /// the same day.
    /// </summary>
    public DateOnly? TradingDay { get; init; }

    /// <summary>
    /// 成交時間(<c>nDealTime</c> HHmmss,台北時間;讀不懂,或是 0 而且沒有成交價(第一則不完整的快照)時為 null)。實測與同一筆成交的
    /// <c>nTimehms</c> 一致(例如 200748)。
    /// The deal time, Taipei; null when unreadable, or zero without a price (a partial first snapshot). Matches the
    /// tick's time as measured.
    /// </summary>
    public TimeOnly? DealTime { get; init; }

    /// <summary>用戶端收到的 UTC 時間。When the client received it, UTC.</summary>
    public required DateTimeOffset ReceivedUtc { get; init; }
}
