using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

// SKCOM 事件:報價與成交轉成 stdout 事件、索引 → 代碼快取、商品與 K 線交給進行中的請求收集。
// SKCOM events: quotes and ticks become stdout events, the index-to-symbol cache, and product and K-line rows go to the
// request collecting them.
internal sealed partial class ServeHost
{
    private readonly Lock _eventGate = new();
    private readonly Dictionary<int, SymbolInfo> _indexCache = [];
    private RowCollector? _productCollector;
    private RowCollector? _detailCollector;
    private KLineCollector? _klineCollector;

    private void HookEvents()
    {
        _source.Announcement += message => _log.Write("公告:" + message);
        _source.Connection += code =>
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"OnConnect({code})"));
            _ = _inbox.Writer.TryWrite(new ConnectItem(code));
        };
        _source.Product += row => CollectRow(ref _productCollector, row);
        _source.ProductDetail += row => CollectRow(ref _detailCollector, row);
        _source.KLine += OnKLine;
        _source.Quote += OnQuote;
        _source.Tick += OnTick;
        _source.DomesticConnection += code =>
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"國內 OnConnection({code})"));
            _ = _inbox.Writer.TryWrite(new DomesticConnectItem(code));
        };
        _source.DomesticKLine += OnDomesticKLine;
        _source.DomesticKLineComplete += OnDomesticKLineComplete;
    }

    private void CollectRow(ref RowCollector? field, string row)
    {
        lock (_eventGate)
        {
            RowCollector? collector = field;

            if (collector is null)
            {
                return;
            }

            if (row.StartsWith("##", StringComparison.Ordinal))
            {
                // 群益以 ## 開頭那筆當結尾(實測 ##,,,,)。Capital ends the list with a row starting with ## (measured: ##,,,,).
                field = null;
                collector.Completed.TrySetResult();
                return;
            }

            collector.Rows.Add(row);
        }
    }

    private void OnKLine(string stockNo, string data)
    {
        lock (_eventGate)
        {
            KLineCollector? collector = _klineCollector;

            // 只收這次要的代碼(不含交易所);其他代碼的行是之前請求的尾巴。Only rows for the code asked this time.
            if (collector is null || !string.Equals(stockNo, collector.Code, StringComparison.Ordinal))
            {
                return;
            }

            collector.Lines.Add(data);
            collector.LastRowTimestamp = _time.GetTimestamp();
        }
    }

    private void OnQuote(int index)
    {
        int code = _source.GetStockByIndex(index, out SkQuoteData quote);

        if (code != 0)
        {
            LogCode(string.Create(CultureInfo.InvariantCulture, $"GetStockByIndexNineDigitLONG({index})"), code);
            return;
        }

        string symbol = quote.ExchangeNo + "," + quote.StockNo;

        lock (_eventGate)
        {
            _indexCache[index] = new SymbolInfo(symbol, quote.Decimals);
        }

        _out.WriteEvent("quote", writer =>
        {
            writer.WriteString("symbol", symbol);
            writer.WriteString("name", quote.StockName);
            writer.WriteNumber("decimals", quote.Decimals);
            writer.WriteNumber("denominator", quote.Denominator);
            writer.WriteNumber("tradingDay", quote.TradingDay);
            writer.WriteNumber("open", quote.Open);
            writer.WriteNumber("high", quote.High);
            writer.WriteNumber("low", quote.Low);
            writer.WriteNumber("close", quote.Close);
            writer.WriteNumber("settle", quote.Settle);
            writer.WriteNumber("ref", quote.Reference);
            writer.WriteNumber("bid", quote.Bid);
            writer.WriteNumber("bidQty", quote.BidQuantity);
            writer.WriteNumber("ask", quote.Ask);
            writer.WriteNumber("askQty", quote.AskQuantity);
            writer.WriteNumber("totalQty", quote.TotalQuantity);
            writer.WriteNumber("tickQty", quote.TickQuantity);
        });
    }

    private void OnTick(SkTickData tick)
    {
        if (!TryResolve(tick.Index, out SymbolInfo info))
        {
            return;
        }

        _out.WriteEvent("tick", writer =>
        {
            writer.WriteString("symbol", info.Symbol);
            writer.WriteNumber("ptr", tick.Pointer);
            writer.WriteString("date", tick.Date.ToString("D8", CultureInfo.InvariantCulture));
            writer.WriteString("time", tick.Time.ToString("D6", CultureInfo.InvariantCulture));
            writer.WriteNumber("price", tick.Price);
            writer.WriteNumber("qty", tick.Quantity);
            writer.WriteBoolean("history", tick.IsHistory);
            writer.WriteNumber("decimals", info.Decimals);
        });
    }

    /// <summary>
    /// 索引 → 代碼:第一次看到某個索引(實測成交常比報價先到)就以 GetStockByIndexNineDigitLONG 取並快取。
    /// Index to symbol: the first time an index shows up (ticks were measured to arrive before quotes) it is looked up
    /// with GetStockByIndexNineDigitLONG and cached.
    /// </summary>
    private bool TryResolve(int index, out SymbolInfo info)
    {
        lock (_eventGate)
        {
            if (_indexCache.TryGetValue(index, out info))
            {
                return true;
            }
        }

        int code = _source.GetStockByIndex(index, out SkQuoteData quote);

        if (code != 0 || string.IsNullOrEmpty(quote.StockNo))
        {
            LogCode(string.Create(CultureInfo.InvariantCulture, $"GetStockByIndexNineDigitLONG({index})"), code);
            info = default;
            return false;
        }

        info = new SymbolInfo(quote.ExchangeNo + "," + quote.StockNo, quote.Decimals);

        lock (_eventGate)
        {
            _indexCache[index] = info;
        }

        return true;
    }

    private void ClearIndexCache()
    {
        lock (_eventGate)
        {
            _indexCache.Clear();
        }
    }

    private readonly record struct SymbolInfo(string Symbol, int Decimals);

    private sealed class RowCollector
    {
        public List<string> Rows { get; } = [];

        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class KLineCollector
    {
        public KLineCollector(string code)
        {
            Code = code;
        }

        public string Code { get; }

        public List<string> Lines { get; } = [];

        public long LastRowTimestamp { get; set; }
    }
}
