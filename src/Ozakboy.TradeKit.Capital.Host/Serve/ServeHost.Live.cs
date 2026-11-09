using System.Globalization;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

// 0.3.0:SKCOM 版本、國內期貨 / 選擇權即時報價(tw-products、tw-subscribe 與 tw-quote / tw-tick / tw-best5 / tw-live-bar 事件),
// 以及海外五檔 / 十檔(depth 與 best5 / best10 事件)。依手冊 V2.13.59 實作,格式、時間語意、欄位位置都待 probe-full 實測。
// 0.3.0: the SKCOM version, domestic live quotes and overseas best 5 / best 10, built from the V2.13.59 manual; formats,
// time semantics and field positions are still to be measured with probe-full.
internal sealed partial class ServeHost
{
    /// <summary>
    /// 國內訂閱上限:手冊 4-4-1「單一 SKQuoteLib 物件,僅一個訂閱 Tick 報價功能(10 檔商品)」;tw-subscribe 每檔都訂成交,所以整組 10 檔(待實測)。
    /// The domestic subscription cap: the manual allows ten tick subscriptions per SKQuoteLib object, and tw-subscribe
    /// subscribes ticks for every code, so the whole set is capped at ten (still to be measured).
    /// </summary>
    internal const int MaxDomesticSubscriptions = 10;

    private const short DomesticFuturesMarket = 2;
    private const short DomesticOptionsMarket = 3;

    private readonly List<string> _domesticSubscriptions = [];
    private readonly Dictionary<(short Market, int Index), DomesticInfo> _domesticIndex = [];
    private readonly Dictionary<string, int> _domesticDecimals = new(StringComparer.Ordinal);
    private readonly List<string> _depthSubscriptions = [];
    private StockListCollector? _stockListCollector;
    private string _loginId = string.Empty;
    private string? _skcomVersion;

    private void HookLiveQuoteEvents()
    {
        _source.DomesticStockList += OnDomesticStockList;
        _source.DomesticQuote += OnDomesticQuote;
        _source.DomesticTick += OnDomesticTick;
        _source.DomesticBest5 += OnDomesticBest5;
        _source.DomesticLiveBar += OnDomesticLiveBar;
        _source.OverseasBest5 += book => OnOverseasBook("best5", book);
        _source.OverseasBest10 += book => OnOverseasBook("best10", book);
    }

    /// <summary>
    /// 讀 SKCOM 版本(登入前,probe-full 的順序);讀到就附在之後每一則 status 事件(<c>skcomVersion</c>)。讀不到只記紀錄。
    /// Reads the SKCOM version before login; once known it is appended to every later status event. A failure is only logged.
    /// </summary>
    private void ReadSkcomVersion()
    {
        string version;

        try
        {
            version = _log.Redact(_source.GetApiVersion(_loginId)).Trim();
        }
#pragma warning disable CA1031 // 只是版本標示:讀不到不能讓啟動流程中斷。Only a version label; a failure must not stop the start-up.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"讀 SKCOM 版本失敗(0x{exception.HResult:X8}),照常繼續。"));
            return;
        }

        if (version.Length == 0)
        {
            _log.Write("SKCOM 版本是空的,照常繼續。");
            return;
        }

        _skcomVersion = version;
        _out.SetStatusSuffix(WriteSkcomVersion);
        _log.Write("SKCOM 版本:" + version);
    }

    private void WriteSkcomVersion(Utf8JsonWriter writer)
    {
        if (_skcomVersion is { } version)
        {
            writer.WriteString("skcomVersion", version);
        }
    }

    // ── tw-products ──

    private async Task HandleDomesticProductsAsync(long id, string line, JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("market", out JsonElement marketElement)
            || marketElement.ValueKind != JsonValueKind.Number
            || !marketElement.TryGetInt16(out short market)
            || market is not (DomesticFuturesMarket or DomesticOptionsMarket))
        {
            _out.WriteError(id, WireErrorCodes.RequestInvalid, "tw-products 需要 market(2 期貨或 3 選擇權)。tw-products needs market 2 (futures) or 3 (options).");
            return;
        }

        if (!PassDomesticGate(id, line))
        {
            return;
        }

        StockListCollector collector = new(market);

        lock (_eventGate)
        {
            _stockListCollector = collector;
        }

        // 手冊 4-4-17:結果由 OnNotifyStockList 分多則送來,以「##」開頭那則結尾。The list arrives in chunks and ends with "##".
        int code = _source.RequestDomesticStockList(market);
        bool completed = false;

        if (code == 0)
        {
            using CancellationTokenSource delayCancel = new();
            Task finished = await Task.WhenAny(collector.Completed.Task, Task.Delay(_timings.ProductsTimeout, _time, delayCancel.Token)).ConfigureAwait(true);
            completed = finished == collector.Completed.Task;
            await delayCancel.CancelAsync().ConfigureAwait(true);
        }

        string[] rows;

        lock (_eventGate)
        {
            _stockListCollector = null;
            rows = [.. collector.Rows];
        }

        if (code != 0)
        {
            string message = _log.Redact(SafeMessage(code));
            _out.WriteError(
                id,
                WireErrorCodes.ProductsRejected,
                string.Create(CultureInfo.InvariantCulture, $"國內商品清單請求被拒(代碼 {code}):{message}。The domestic product request was rejected (code {code})."),
                writer =>
                {
                    writer.WriteNumber("skCode", code);
                    writer.WriteString("skMessage", message);
                });
            return;
        }

        if (!completed)
        {
            _out.WriteError(
                id,
                WireErrorCodes.ProductsTimeout,
                string.Create(CultureInfo.InvariantCulture, $"國內商品清單在 {_timings.ProductsTimeout.TotalSeconds:F0} 秒內沒有收到結尾。The domestic product list did not end in time."),
                writer => writer.WriteNumber("received", rows.Length));
            return;
        }

        _out.WriteResult(id, writer =>
        {
            writer.WriteNumber("market", market);
            WriteStringArray(writer, "rows", rows);
        });
    }

    private void OnDomesticStockList(short market, string data)
    {
        lock (_eventGate)
        {
            StockListCollector? collector = _stockListCollector;

            if (collector is null || collector.Market != market)
            {
                return;
            }

            // 手冊 4-4-h:一則可能含多筆(「;」分隔)、多個類股(換行分隔);「##」開頭那則是結尾。
            // One chunk may hold several entries (';') and groups (new lines); a chunk starting with ## ends the list.
            foreach (string entry in data.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (entry.StartsWith("##", StringComparison.Ordinal))
                {
                    _ = collector.Completed.TrySetResult();
                    return;
                }

                collector.Rows.Add(entry);
            }
        }
    }

    // ── tw-subscribe ──

    private void HandleDomesticSubscribe(long id, string line, JsonElement parameters)
    {
        if (!TryReadStringArray(parameters, "codes", IsValidDomesticCode, out List<string> codes) || codes.Count == 0)
        {
            _out.WriteError(id, WireErrorCodes.RequestInvalid, "tw-subscribe 需要 codes 陣列(國內商品代碼)。tw-subscribe needs a codes array of domestic codes.");
            return;
        }

        if (_domesticPhase is DomesticPhase.Idle or DomesticPhase.Connecting)
        {
            // 第一次連線還沒有結果:等到 3003 或不可用再處理。The first connect has no outcome yet.
            _domesticDeferred.Enqueue(line);
            return;
        }

        if (_domesticPhase == DomesticPhase.Unavailable)
        {
            WriteDomesticUnavailable(id);
            return;
        }

        List<string> fresh = [.. codes.Where(code => !_domesticSubscriptions.Contains(code, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)];

        if (_domesticSubscriptions.Count + fresh.Count > MaxDomesticSubscriptions)
        {
            _out.WriteError(
                id,
                WireErrorCodes.SubscribeLimitExceeded,
                string.Create(CultureInfo.InvariantCulture, $"國內訂閱最多 {MaxDomesticSubscriptions} 檔(手冊 4-4-1)。At most {MaxDomesticSubscriptions} domestic codes can be subscribed."),
                writer =>
                {
                    writer.WriteNumber("limit", MaxDomesticSubscriptions);
                    writer.WriteNumber("requested", _domesticSubscriptions.Count + fresh.Count);
                });
            return;
        }

        if (fresh.Count > 0 && _domesticPhase == DomesticPhase.Ready && !SendDomesticSubscriptions(id, fresh))
        {
            return;
        }

        if (_domesticPhase != DomesticPhase.Ready)
        {
            // 斷線中:先記住,3003 之後隨全部訂閱一起送。While disconnected: remembered and sent with the rest after 3003.
            _domesticSubscriptions.AddRange(fresh);
        }

        _out.WriteResult(id, writer => WriteStringArray(writer, "subscribed", _domesticSubscriptions));
    }

    /// <summary>
    /// 新的代碼先查索引與小數位數,再把完整集合重送一次 RequestStocks(手冊:一個物件只有一組即時報價訂閱),每個新代碼一頁 RequestTicks。
    /// Looks up the new codes' index and decimals, re-sends RequestStocks with the whole set (one quote subscription per
    /// object, per the manual) and gives every new code its own RequestTicks page.
    /// </summary>
    private bool SendDomesticSubscriptions(long id, List<string> fresh)
    {
        foreach (string code in fresh)
        {
            if (!ResolveDomesticCode(code, out int lookupCode))
            {
                WriteSubscribeRejected(id, lookupCode, code);
                return false;
            }
        }

        List<string> all = [.. _domesticSubscriptions, .. fresh];
        int stocks = _source.RequestDomesticStocks(string.Join(',', all));

        if (stocks != 0)
        {
            WriteSubscribeRejected(id, stocks, string.Join(',', fresh));
            return false;
        }

        _domesticSubscriptions.AddRange(fresh);

        foreach (string code in fresh)
        {
            short page = (short)_domesticSubscriptions.IndexOf(code);
            int ticks = _source.RequestDomesticTicks(page, code);

            if (ticks != 0)
            {
                // 報價已訂上(留在集合裡,重連時會再試成交);這次回報失敗。Quotes are on; the tick failure is reported.
                WriteSubscribeRejected(id, ticks, code);
                return false;
            }
        }

        return true;
    }

    /// <summary>國內 3003 之後:索引重查,完整集合重送。After a domestic 3003: indexes looked up again, the whole set re-sent.</summary>
    private void ResendDomesticSubscriptions()
    {
        if (_domesticSubscriptions.Count == 0)
        {
            return;
        }

        ClearDomesticIndex();

        foreach (string code in _domesticSubscriptions)
        {
            if (!ResolveDomesticCode(code, out int lookupCode))
            {
                LogCode("重查國內商品 " + code, lookupCode == 0 ? -1 : lookupCode);
            }
        }

        LogCode("重送國內 RequestStocks", _source.RequestDomesticStocks(string.Join(',', _domesticSubscriptions)));

        for (int page = 0; page < _domesticSubscriptions.Count; page++)
        {
            LogCode("重送國內 RequestTicks " + _domesticSubscriptions[page], _source.RequestDomesticTicks((short)page, _domesticSubscriptions[page]));
        }

        _log.Write(string.Create(CultureInfo.InvariantCulture, $"國內訂閱已重送({_domesticSubscriptions.Count} 檔)。"));
    }

    /// <summary>
    /// 代碼 → (市場別, 索引, 小數位數):先試期貨(2)再試選擇權(3)(<c>GetStockByMarketAndNo</c>,手冊 4-4-33;沒訂報價時只有基本資料,夠用)。
    /// Code to market, index and decimals: futures first, then options.
    /// </summary>
    private bool ResolveDomesticCode(string code, out int lookupCode)
    {
        lookupCode = 0;

        foreach (short market in (ReadOnlySpan<short>)[DomesticFuturesMarket, DomesticOptionsMarket])
        {
            lookupCode = _source.GetDomesticStockByCode(market, code, out SkDomesticStock stock);

            if (lookupCode == 0 && !string.IsNullOrEmpty(stock.StockNo))
            {
                lock (_eventGate)
                {
                    _domesticIndex[(market, stock.Index)] = new DomesticInfo(code, stock.Decimals);
                    _domesticDecimals[code] = stock.Decimals;
                }

                return true;
            }
        }

        return false;
    }

    private void ClearDomesticIndex()
    {
        lock (_eventGate)
        {
            _domesticIndex.Clear();
        }
    }

    /// <summary>
    /// 國內 (市場別, 索引) → 代碼。快取沒有時以 <c>GetStockByIndexLONG</c> 取基本資料並快取(手冊 4-4-l 建議不要在成交事件裡呼叫它;
    /// 訂閱時已先查過索引,這裡只是保險,待實測)。
    /// Domestic market and index to code; a cache miss reads the basic data with GetStockByIndexLONG and caches it. The
    /// manual advises against calling it inside tick events; subscribing already looked the index up, so this is a
    /// safety net only (still to be measured).
    /// </summary>
    private bool TryResolveDomestic(short market, int index, out DomesticInfo info)
    {
        lock (_eventGate)
        {
            if (_domesticIndex.TryGetValue((market, index), out info))
            {
                return true;
            }
        }

        int code = _source.GetDomesticStockByIndex(market, index, out SkDomesticStock stock);

        if (code != 0 || string.IsNullOrEmpty(stock.StockNo))
        {
            LogCode(string.Create(CultureInfo.InvariantCulture, $"國內 GetStockByIndexLONG({market}, {index})"), code == 0 ? -1 : code);
            info = default;
            return false;
        }

        info = new DomesticInfo(stock.StockNo, stock.Decimals);

        lock (_eventGate)
        {
            _domesticIndex[(market, index)] = info;
            _domesticDecimals.TryAdd(stock.StockNo, stock.Decimals);
        }

        return true;
    }

    private void OnDomesticQuote(short market, int index)
    {
        int code = _source.GetDomesticStockByIndex(market, index, out SkDomesticStock stock);

        if (code != 0)
        {
            LogCode(string.Create(CultureInfo.InvariantCulture, $"國內 GetStockByIndexLONG({market}, {index})"), code);
            return;
        }

        string symbol;

        lock (_eventGate)
        {
            // 訂閱時記下的代碼優先(呼叫端以它識別);沒有才用群益給的。The subscribed code wins; otherwise Capital's.
            symbol = _domesticIndex.TryGetValue((market, index), out DomesticInfo known) ? known.Code : stock.StockNo;
            _domesticIndex[(market, index)] = new DomesticInfo(symbol, stock.Decimals);
            _domesticDecimals[symbol] = stock.Decimals;
        }

        _out.WriteEvent("tw-quote", writer =>
        {
            writer.WriteString("code", symbol);
            writer.WriteNumber("market", market);
            writer.WriteNumber("index", index);
            writer.WriteString("marketNo", stock.MarketNo);
            writer.WriteString("stockNo", stock.StockNo);
            writer.WriteString("name", stock.StockName);
            writer.WriteString("stockNoSpread", stock.StockNoSpread);
            writer.WriteNumber("decimals", stock.Decimals);
            writer.WriteNumber("typeNo", stock.TypeNo);
            writer.WriteNumber("open", stock.Open);
            writer.WriteNumber("high", stock.High);
            writer.WriteNumber("low", stock.Low);
            writer.WriteNumber("close", stock.Close);
            writer.WriteNumber("tickQty", stock.TickQuantity);
            writer.WriteNumber("ref", stock.Reference);
            writer.WriteNumber("bid", stock.Bid);
            writer.WriteNumber("bidQty", stock.BidQuantity);
            writer.WriteNumber("ask", stock.Ask);
            writer.WriteNumber("askQty", stock.AskQuantity);
            writer.WriteNumber("buyQty", stock.BuyQuantity);
            writer.WriteNumber("sellQty", stock.SellQuantity);
            writer.WriteNumber("openInterest", stock.OpenInterest);
            writer.WriteNumber("totalQty", stock.TotalQuantity);
            writer.WriteNumber("yesterdayQty", stock.YesterdayQuantity);
            writer.WriteNumber("up", stock.LimitUp);
            writer.WriteNumber("down", stock.LimitDown);
            writer.WriteNumber("simulate", stock.Simulate);
            writer.WriteNumber("dayTrade", stock.DayTrade);
            writer.WriteNumber("tradingDay", stock.TradingDay);
            writer.WriteNumber("tradingLotFlag", stock.TradingLotFlag);
            writer.WriteNumber("dealTime", stock.DealTime);
            writer.WriteNumber("oddLotBid", stock.OddLotBid);
            writer.WriteNumber("oddLotAsk", stock.OddLotAsk);
            writer.WriteNumber("oddLotClose", stock.OddLotClose);
            writer.WriteNumber("oddLotQty", stock.OddLotQuantity);
        });
    }

    private void OnDomesticTick(SkDomesticTick tick)
    {
        if (!TryResolveDomestic(tick.Market, tick.Index, out DomesticInfo info))
        {
            return;
        }

        _out.WriteEvent("tw-tick", writer =>
        {
            writer.WriteString("code", info.Code);
            writer.WriteNumber("market", tick.Market);
            writer.WriteNumber("ptr", tick.Pointer);
            writer.WriteNumber("date", tick.Date);
            writer.WriteNumber("time", tick.TimeHms);
            writer.WriteNumber("micros", tick.TimeMicros);
            writer.WriteNumber("bid", tick.Bid);
            writer.WriteNumber("ask", tick.Ask);
            writer.WriteNumber("close", tick.Close);
            writer.WriteNumber("qty", tick.Quantity);
            writer.WriteNumber("simulate", tick.Simulate);
            writer.WriteBoolean("history", tick.IsHistory);
            writer.WriteNumber("decimals", info.Decimals);
        });
    }

    private void OnDomesticBest5(SkBook book)
    {
        if (!TryResolveDomestic(book.Market, book.Index, out DomesticInfo info))
        {
            return;
        }

        WriteBook("tw-best5", "code", info.Code, info.Decimals, book);
    }

    private void OnDomesticLiveBar(SkLiveBar bar)
    {
        int? decimals;

        lock (_eventGate)
        {
            decimals = _domesticDecimals.TryGetValue(bar.StockNo, out int known) ? known : null;
        }

        _out.WriteEvent("tw-live-bar", writer =>
        {
            writer.WriteString("code", bar.StockNo);
            writer.WriteNumber("type", bar.Type);
            writer.WriteNumber("date", bar.Date);
            writer.WriteNumber("time", bar.TimeHm);
            writer.WriteNumber("open", bar.Open);
            writer.WriteNumber("high", bar.High);
            writer.WriteNumber("low", bar.Low);
            writer.WriteNumber("close", bar.Close);
            writer.WriteNumber("qty", bar.Quantity);

            // 小數位數不知道(還沒查到這個代碼)就不寫,用戶端的價格為 null,不猜。Unknown decimals are omitted rather than guessed.
            if (decimals is { } value)
            {
                writer.WriteNumber("decimals", value);
            }
        });
    }

    // ── depth(海外十檔)與 best5 / best10 事件 ──

    private void HandleDepth(long id, JsonElement parameters)
    {
        if (!TryReadStringArray(parameters, "symbols", IsValidSymbol, out List<string> symbols) || symbols.Count == 0)
        {
            _out.WriteError(id, WireErrorCodes.RequestInvalid, "depth 需要 symbols 陣列(交易所,代碼)。depth needs a symbols array of \"exchange,code\".");
            return;
        }

        List<string> fresh = [.. symbols.Where(symbol => !_depthSubscriptions.Contains(symbol, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)];

        if (_phase == Phase.Ready)
        {
            foreach (string symbol in fresh)
            {
                int code = _source.RequestMarketDepth(symbol);

                if (code != 0)
                {
                    WriteSubscribeRejected(id, code, symbol);
                    return;
                }

                _depthSubscriptions.Add(symbol);
            }
        }
        else
        {
            // 斷線中:先記住,重連後重送。While disconnected: remembered and re-sent after the reconnect.
            _depthSubscriptions.AddRange(fresh);
        }

        _out.WriteResult(id, writer => WriteStringArray(writer, "depth", _depthSubscriptions));
    }

    private void ResendDepth()
    {
        foreach (string symbol in _depthSubscriptions)
        {
            LogCode("重送 RequestMarketDepth " + symbol, _source.RequestMarketDepth(symbol));
        }
    }

    private void OnOverseasBook(string eventName, SkBook book)
    {
        if (!TryResolve(book.Index, out SymbolInfo info))
        {
            return;
        }

        WriteBook(eventName, "symbol", info.Symbol, info.Decimals, book);
    }

    /// <summary>
    /// 寫一則檔位事件:價格是原始整數(用戶端 ÷ 10^decimals),國內五檔另帶衍生一檔與試算旗標。
    /// Writes one order book event with raw integer prices; the domestic best 5 also carries the extended level and the
    /// simulate flag.
    /// </summary>
    private void WriteBook(string eventName, string keyName, string key, int decimals, SkBook book) =>
        _out.WriteEvent(eventName, writer =>
        {
            writer.WriteString(keyName, key);

            if (book.Market >= 0)
            {
                writer.WriteNumber("market", book.Market);
            }

            writer.WriteNumber("decimals", decimals);
            WriteNumberArray(writer, "bidPrices", book.BidPrices);
            WriteNumberArray(writer, "bidQuantities", book.BidQuantities);
            WriteNumberArray(writer, "askPrices", book.AskPrices);
            WriteNumberArray(writer, "askQuantities", book.AskQuantities);

            if (book.Extended is { } extended)
            {
                writer.WriteNumber("extendBid", extended.Bid);
                writer.WriteNumber("extendBidQty", extended.BidQuantity);
                writer.WriteNumber("extendAsk", extended.Ask);
                writer.WriteNumber("extendAskQty", extended.AskQuantity);
            }

            if (book.Simulate is { } simulate)
            {
                writer.WriteNumber("simulate", simulate);
            }
        });

    /// <summary>國內請求的閘門:首次連線中先存著(回 false),不可用或斷線回錯誤(回 false),就緒回 true。The domestic gate.</summary>
    private bool PassDomesticGate(long id, string line)
    {
        if (_domesticPhase is DomesticPhase.Idle or DomesticPhase.Connecting)
        {
            _domesticDeferred.Enqueue(line);
            return false;
        }

        if (_domesticPhase != DomesticPhase.Ready)
        {
            WriteDomesticUnavailable(id);
            return false;
        }

        return true;
    }

    private static bool TryReadStringArray(JsonElement parameters, string name, Func<string, bool> isValid, out List<string> values)
    {
        values = [];

        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty(name, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement element in array.EnumerateArray())
        {
            string? value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

            if (value is null || !isValid(value))
            {
                return false;
            }

            values.Add(value);
        }

        return true;
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);

        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteNumberArray(Utf8JsonWriter writer, string name, IReadOnlyList<long> values)
    {
        writer.WriteStartArray(name);

        foreach (long value in values)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
    }

    private readonly record struct DomesticInfo(string Code, int Decimals);

    private sealed class StockListCollector
    {
        public StockListCollector(short market)
        {
            Market = market;
        }

        public short Market { get; }

        public List<string> Rows { get; } = [];

        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
