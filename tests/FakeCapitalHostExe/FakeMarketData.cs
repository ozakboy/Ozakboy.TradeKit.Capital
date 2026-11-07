using Ozakboy.TradeKit.Capital.Host.Serve;

namespace FakeCapitalHostExe;

/// <summary>
/// 假宿主的行情資料,全部抄自 2026-11-06 探針(<c>Host probe</c>)的實測輸出(products.txt、product-detail.txt、
/// probe.jsonl 的 quote 事件、ticks.txt、kline.txt),只取少數幾筆。
/// The fake host's market data, copied in small amounts from the 2026-11-06 probe output.
/// </summary>
internal static class FakeMarketData
{
    /// <summary>商品清單(實測格式,以 <c>##,,,,</c> 結尾)。The product list, ending with "##,,,,".</summary>
    public static readonly string[] Products =
    [
        "CBOT,芝加哥期貨交易所,YM0000,小道熱2612,20261218,20261218",
        "CME,芝加哥商品交易所,NQ0000,小那熱2612,20261218,20261218",
        "FX,國際外匯,SUSDTWD,新台幣,99991231,0",
        "FX,國際外匯,SUSDJPY,日元,99991231,0",
        "FX,國際外匯,SEURUSD,歐元,99991231,0",
        "FX,國際外匯,SUSDCNY,人民幣在岸即時,99991231,0",
        "INDEX,國際指數,NI225,日經指數-延遲,99991231,0",
        "NYM,紐約商業交易所,GC0000,黃金熱2612,20261229,20261130",
        "##,,,,",
    ];

    /// <summary>商品明細(實測格式,以 <c>##,,,,,,</c> 結尾)。The product details, ending with "##,,,,,,".</summary>
    public static readonly string[] ProductDetails =
    [
        "CBOT,芝加哥期貨交易所,YM0000,小道熱2612,CBT,YM_202612,20261218,20261218,5",
        "CME,芝加哥商品交易所,NQ0000,小那熱2612,CME,NQ_202612,20261218,20261218,5",
        "FX,國際外匯,SUSDTWD,新台幣,--,--,99991231,0,2",
        "FX,國際外匯,SUSDJPY,日元,--,--,99991231,0,2",
        "FX,國際外匯,SEURUSD,歐元,--,--,99991231,0,2",
        "FX,國際外匯,SUSDCNY,人民幣在岸即時,--,--,99991231,0,2",
        "INDEX,國際指數,NI225,日經指數-延遲,--,--,99991231,0,1",
        "NYM,紐約商業交易所,GC0000,黃金熱2612,NYM,GC_202612,20261229,20261130,5",
        "##,,,,,,",
    ];

    /// <summary>
    /// 報價(probe.jsonl 的 quote 事件:索引、小數位數、價格原始整數、交易日)與回補成交(ticks.txt 的 H 列:ptr、日期、時間、價格、量;
    /// SUSDJPY 最後一筆 20261105 224357 與 NQ0000 的 214356 是實測當下的即時成交,這裡當成回補的最後一筆)。
    /// SEURUSD 與 SUSDCNY 的成交是依同一格式構造的。
    /// Quotes from the probe's quote events and replayed ticks from ticks.txt; the last SUSDJPY and NQ0000 rows were live
    /// ticks at measurement time and serve as the last replayed ones here. The SEURUSD and SUSDCNY ticks are built in the
    /// same format.
    /// </summary>
    public static readonly FakeInstrument[] Instruments =
    [
        new(143, "FX", "SUSDTWD", "新台幣", 3, new SkQuoteData { Open = 33071, High = 33152, Low = 32947, Close = 32937, Settle = 33067, Reference = 33067, Bid = 32937, Ask = 32967, TotalQuantity = 5716, TickQuantity = 1 },
            [(540, 20261105, 162219, 33047, 1), (541, 20261105, 162220, 33046, 1), (7061, 20261105, 224356, 32941, 1)]),
        new(137, "FX", "SUSDJPY", "日元", 2, new SkQuoteData { Open = 15767, High = 15800, Low = 15726, Close = 15786, Settle = 15766, Reference = 15766, Bid = 15786, Ask = 15788, TotalQuantity = 50730, TickQuantity = 1 },
            [(270, 20261105, 160154, 15782, 1), (271, 20261105, 160154, 15782, 1), (57842, 20261105, 224357, 15784, 1)]),
        new(142, "FX", "SEURUSD", "歐元", 4, new SkQuoteData { Open = 11217, High = 11234, Low = 11201, Close = 11200, Settle = 11218, Reference = 11218, Bid = 11200, Ask = 11202, TotalQuantity = 21208, TickQuantity = 1 },
            [(1, 20261105, 160004, 11217, 1), (2, 20261105, 160007, 11218, 1), (3, 20261105, 160010, 11219, 1)]),
        new(147, "FX", "SUSDCNY", "人民幣在岸即時", 4, new SkQuoteData { Open = 66682, High = 66702, Low = 66686, Close = 66683, Settle = 66680, Reference = 66680, Bid = 66683, Ask = 66692, TotalQuantity = 1215, TickQuantity = 1 },
            [(1, 20261105, 160004, 66682, 1), (2, 20261105, 160007, 66683, 1), (3, 20261105, 160010, 66684, 1)]),
        new(310, "CME", "NQ0000", "小那熱2612", 2, new SkQuoteData { Open = 2664025, High = 2715925, Low = 2636650, Close = 2698375, Settle = 2675200, Reference = 2675200, Bid = 2698175, BidQuantity = 1, Ask = 2698375, AskQuantity = 1, TotalQuantity = 102725, TickQuantity = 1 },
            [(0, 20261105, 170000, 2664025, 26), (1, 20261105, 170000, 2664450, 1), (81874, 20261105, 214356, 2696450, 1)]),
        new(682, "CBOT", "YM0000", "小道熱2612", 0, new SkQuoteData { Open = 51365, High = 51569, Low = 51161, Close = 51176, Settle = 51615, Reference = 51615, Bid = 51173, BidQuantity = 1, Ask = 51177, AskQuantity = 3, TotalQuantity = 14339, TickQuantity = 1 },
            [(940, 20261105, 171929, 51312, 1), (941, 20261105, 171931, 51318, 1), (11218, 20261105, 214352, 51161, 1)]),
        new(543, "NYM", "GC0000", "黃金熱2612", 1, new SkQuoteData { Open = 40985, High = 41162, Low = 40496, Close = 40518, Settle = 41135, Reference = 41135, Bid = 40515, BidQuantity = 2, Ask = 40518, AskQuantity = 1, TotalQuantity = 33640, TickQuantity = 1 },
            [(940, 20261105, 181559, 40891, 1), (941, 20261105, 181559, 40890, 2), (24725, 20261105, 224350, 40546, 1)]),
    ];

    /// <summary>
    /// 1 分 K(kline.txt;時間是那一分鐘的結束,外匯第一根 16:01、CME 17:01;價格是單精度浮點文字)。
    /// One-minute rows from kline.txt: the time ends the minute and prices are single-precision float text.
    /// </summary>
    public static readonly Dictionary<string, string[]> MinuteBars = new(StringComparer.Ordinal)
    {
        ["SUSDJPY"] =
        [
            "2026/11/02 16:01, 157.889999, 157.940002, 157.889999, 157.940002, 141",
            "2026/11/02 16:02, 157.919998, 157.940002, 157.910004, 157.919998, 108",
            "2026/11/02 16:03, 157.919998, 157.940002, 157.910004, 157.919998, 133",
        ],
        ["SUSDTWD"] =
        [
            "2026/11/02 16:01, 32.598000, 32.598999, 32.598000, 32.598999, 9",
            "2026/11/02 16:02, 32.598000, 32.598000, 32.597000, 32.597000, 15",
        ],
        ["NQ0000"] =
        [
            "2026/11/02 17:01, 28201.000000, 28212.750000, 28165.000000, 28203.000000, 679",
            "2026/11/02 17:02, 28203.250000, 28204.000000, 28151.000000, 28156.250000, 559",
        ],
    };

    /// <summary>日 K(kline.txt;只有日期)。Daily rows from kline.txt, date only.</summary>
    public static readonly Dictionary<string, string[]> DailyBars = new(StringComparer.Ordinal)
    {
        ["SUSDJPY"] =
        [
            "2026/10/07, 158.089996, 158.500000, 157.860001, 158.080002, 142254",
            "2026/10/08, 158.029999, 158.360001, 157.529999, 157.910004, 168181",
        ],
    };
}

/// <summary>
/// 一檔假商品。
/// One fake instrument.
/// </summary>
internal sealed record FakeInstrument(
    int Index,
    string Exchange,
    string Code,
    string Name,
    int Decimals,
    SkQuoteData Quote,
    (int Pointer, int Date, int Time, long Price, int Quantity)[] HistoryTicks)
{
    public string Symbol => Exchange + "," + Code;
}
