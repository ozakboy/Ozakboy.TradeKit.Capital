namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 檔位資料來自哪一個報價物件。
/// Which quote object an order book came from.
/// </summary>
public enum CapitalQuoteMarket
{
    /// <summary>國內期貨 / 選擇權(SKQuoteLib)。Domestic futures and options.</summary>
    Domestic = 0,

    /// <summary>海外期貨 / 外匯(SKOSQuoteLib)。Overseas futures and FX.</summary>
    Overseas = 1,

    /// <summary>海外選擇權(SKOOQuoteLib)。Overseas options.</summary>
    OverseasOptions = 2,
}
