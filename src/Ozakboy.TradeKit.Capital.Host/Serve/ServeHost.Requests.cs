using System.Globalization;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

// stdin 請求:products / subscribe / kline / status / shutdown。依序處理,一次一個。
// stdin requests, handled one at a time.
internal sealed partial class ServeHost
{
    private readonly List<string> _subscriptions = [];
    private readonly HashSet<string> _subscriptionSet = new(StringComparer.Ordinal);

    /// <summary>處理一個請求;回傳 <see langword="true"/> 表示收到 shutdown。Handles one request; true means shutdown.</summary>
    private async Task<bool> HandleRequestAsync(string line)
    {
        if (!TryParseRequest(line, out long? id, out string method, out JsonElement parameters, out string problem))
        {
            _out.WriteError(id, WireErrorCodes.RequestInvalid, problem);
            return false;
        }

        long requestId = id!.Value;

        switch (method)
        {
            case "products":
                await HandleProductsAsync(requestId).ConfigureAwait(true);
                return false;
            case "subscribe":
                HandleSubscribe(requestId, parameters);
                return false;
            case "kline":
                await HandleKLineAsync(requestId, parameters).ConfigureAwait(true);
                return false;
            case "status":
                HandleStatus(requestId);
                return false;
            case "shutdown":
                LogCode("LeaveMonitor", _source.LeaveMonitor());
                _out.WriteResult(requestId, _ => { });
                _log.Write("收到 shutdown,結束。");
                return true;
            default:
                _out.WriteError(requestId, WireErrorCodes.UnknownMethod, $"不認得的 method:{method}。Unknown method: {method}.");
                return false;
        }
    }

    private static bool TryParseRequest(string line, out long? id, out string method, out JsonElement parameters, out string problem)
    {
        id = null;
        method = string.Empty;
        parameters = default;

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            problem = "請求不是合法的 JSON。The request is not valid JSON.";
            return false;
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                problem = "請求必須是 JSON 物件。The request must be a JSON object.";
                return false;
            }

            if (!root.TryGetProperty("id", out JsonElement idElement)
                || idElement.ValueKind != JsonValueKind.Number
                || !idElement.TryGetInt64(out long idValue))
            {
                problem = "請求缺少整數 id。The request lacks an integer id.";
                return false;
            }

            id = idValue;

            if (!root.TryGetProperty("method", out JsonElement methodElement) || methodElement.ValueKind != JsonValueKind.String)
            {
                problem = "請求缺少 method。The request lacks a method.";
                return false;
            }

            method = methodElement.GetString() ?? string.Empty;

            if (root.TryGetProperty("params", out JsonElement paramsElement))
            {
                if (paramsElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                {
                    problem = "params 必須是物件。params must be an object.";
                    return false;
                }

                parameters = paramsElement.ValueKind == JsonValueKind.Object ? paramsElement.Clone() : default;
            }

            problem = string.Empty;
            return true;
        }
    }

    private bool RequireConnected(long id)
    {
        if (_phase == Phase.Ready)
        {
            return true;
        }

        _out.WriteError(id, WireErrorCodes.NotConnected, "海外報價主機目前沒有連線。The overseas quote server is not connected right now.");
        return false;
    }

    private async Task HandleProductsAsync(long id)
    {
        if (!RequireConnected(id))
        {
            return;
        }

        List<string>? products = await CollectRowsAsync(isDetail: false).ConfigureAwait(true);

        if (products is null)
        {
            WriteProductsFailure(id, "商品清單");
            return;
        }

        List<string>? details = await CollectRowsAsync(isDetail: true).ConfigureAwait(true);

        if (details is null)
        {
            WriteProductsFailure(id, "商品明細");
            return;
        }

        IReadOnlyList<ProductRow> merged = ProductRows.Merge(products, details, out int skipped);

        if (skipped > 0)
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"商品清單有 {skipped} 筆欄位數不足,略過。"));
        }

        _out.WriteResult(id, writer =>
        {
            writer.WriteStartArray("items");

            foreach (ProductRow row in merged)
            {
                writer.WriteStartObject();
                writer.WriteString("exchange", row.Exchange);
                writer.WriteString("exchangeName", row.ExchangeName);
                writer.WriteString("code", row.Code);
                writer.WriteString("name", row.Name);
                writer.WriteString("lastTradeDate", row.LastTradeDate);
                writer.WriteString("firstNoticeDate", row.FirstNoticeDate);

                if (row.Kind is { } kind)
                {
                    writer.WriteNumber("kind", kind);
                }
                else
                {
                    writer.WriteNull("kind");
                }

                WriteNullableString(writer, "orderExchange", row.OrderExchange);
                WriteNullableString(writer, "orderCode", row.OrderCode);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private int _lastProductsFailureCode;

    private void WriteProductsFailure(long id, string what)
    {
        if (_lastProductsFailureCode != 0)
        {
            int code = _lastProductsFailureCode;
            string message = _log.Redact(SafeMessage(code));
            _out.WriteError(
                id,
                WireErrorCodes.ProductsRejected,
                string.Create(CultureInfo.InvariantCulture, $"{what}請求被拒(代碼 {code}):{message}。The product request was rejected (code {code})."),
                writer =>
                {
                    writer.WriteNumber("skCode", code);
                    writer.WriteString("skMessage", message);
                });
            return;
        }

        _out.WriteError(
            id,
            WireErrorCodes.ProductsTimeout,
            string.Create(CultureInfo.InvariantCulture, $"{what}在 {_timings.ProductsTimeout.TotalSeconds:F0} 秒內沒有收到結尾。The product list did not end within the time limit."));
    }

    /// <summary>
    /// 送出清單或明細請求並收到 ## 為止;被拒或逾時回 <see langword="null"/>(原因記在 <see cref="_lastProductsFailureCode"/>)。
    /// Requests the list or the details and collects until ##; null on rejection or timeout.
    /// </summary>
    private async Task<List<string>?> CollectRowsAsync(bool isDetail)
    {
        RowCollector collector = new();
        _lastProductsFailureCode = 0;

        lock (_eventGate)
        {
            if (isDetail)
            {
                _detailCollector = collector;
            }
            else
            {
                _productCollector = collector;
            }
        }

        int code = isDetail ? _source.GetOverseaProductDetail(1) : _source.RequestOverseaProducts();
        bool completed = false;

        if (code == 0)
        {
            Task finished = await Task.WhenAny(collector.Completed.Task, Task.Delay(_timings.ProductsTimeout, _time)).ConfigureAwait(true);
            completed = finished == collector.Completed.Task;
        }
        else
        {
            _lastProductsFailureCode = code;
        }

        lock (_eventGate)
        {
            if (isDetail)
            {
                _detailCollector = null;
            }
            else
            {
                _productCollector = null;
            }

            return completed ? [.. collector.Rows] : null;
        }
    }

    private void HandleSubscribe(long id, JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("symbols", out JsonElement symbolsElement)
            || symbolsElement.ValueKind != JsonValueKind.Array)
        {
            _out.WriteError(id, WireErrorCodes.RequestInvalid, "subscribe 需要 symbols 陣列。subscribe needs a symbols array.");
            return;
        }

        List<string> fresh = [];

        foreach (JsonElement element in symbolsElement.EnumerateArray())
        {
            string? symbol = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

            if (symbol is null || !IsValidSymbol(symbol))
            {
                _out.WriteError(id, WireErrorCodes.RequestInvalid, "商品代碼格式必須是「交易所,代碼」。A symbol must be \"exchange,code\".");
                return;
            }

            if (!_subscriptionSet.Contains(symbol) && !fresh.Contains(symbol, StringComparer.Ordinal))
            {
                fresh.Add(symbol);
            }
        }

        if (fresh.Count > 0 && _phase == Phase.Ready)
        {
            int code = _source.RequestStocks(string.Join('#', fresh));

            if (code != 0)
            {
                WriteSubscribeRejected(id, code, string.Join('#', fresh));
                return;
            }

            AddSubscriptions(fresh);

            foreach (string symbol in fresh)
            {
                int tickCode = _source.RequestTicks(symbol);

                if (tickCode != 0)
                {
                    // 報價已訂上(留在集合裡,重連時會再試成交);這次回報失敗讓呼叫端知道成交沒訂到。
                    // Quotes are subscribed (kept in the set, ticks are retried on reconnect); the failure is reported.
                    WriteSubscribeRejected(id, tickCode, symbol);
                    return;
                }
            }
        }
        else if (fresh.Count > 0)
        {
            // 斷線中:先記住,重連後隨全部訂閱一起送出。While disconnected: remembered and sent with the rest on reconnect.
            AddSubscriptions(fresh);
        }

        _out.WriteResult(id, writer =>
        {
            writer.WriteStartArray("subscribed");

            foreach (string symbol in _subscriptions)
            {
                writer.WriteStringValue(symbol);
            }

            writer.WriteEndArray();
        });
    }

    private void AddSubscriptions(List<string> symbols)
    {
        foreach (string symbol in symbols)
        {
            if (_subscriptionSet.Add(symbol))
            {
                _subscriptions.Add(symbol);
            }
        }
    }

    private void WriteSubscribeRejected(long id, int code, string symbols)
    {
        string message = _log.Redact(SafeMessage(code));
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"訂閱 {symbols} 被拒(代碼 {code}):{message}"));
        _out.WriteError(
            id,
            WireErrorCodes.SubscribeRejected,
            string.Create(CultureInfo.InvariantCulture, $"訂閱被拒(代碼 {code}):{message}。The subscription was rejected (code {code})."),
            writer =>
            {
                writer.WriteNumber("skCode", code);
                writer.WriteString("skMessage", message);
                writer.WriteString("symbol", symbols);
            });
    }

    private void ResendSubscriptions()
    {
        if (_subscriptions.Count == 0)
        {
            return;
        }

        LogCode("重送 RequestStocks", _source.RequestStocks(string.Join('#', _subscriptions)));

        foreach (string symbol in _subscriptions)
        {
            LogCode("重送 RequestTicks " + symbol, _source.RequestTicks(symbol));
        }
    }

    private async Task HandleKLineAsync(long id, JsonElement parameters)
    {
        if (!TryReadKLineParameters(parameters, out string symbol, out short type, out string start, out string end))
        {
            _out.WriteError(
                id,
                WireErrorCodes.RequestInvalid,
                "kline 需要 symbol(交易所,代碼)、type(0–3)、start 與 end(yyyyMMdd)。kline needs symbol, type (0-3), start and end (yyyyMMdd).");
            return;
        }

        if (!RequireConnected(id))
        {
            return;
        }

        KLineCollector collector = new(symbol[(symbol.IndexOf(',', StringComparison.Ordinal) + 1)..]);

        lock (_eventGate)
        {
            _klineCollector = collector;
        }

        int code = _source.RequestKLineByDate(symbol, type, start, end, 1);

        if (code != 0)
        {
            lock (_eventGate)
            {
                _klineCollector = null;
            }

            string message = _log.Redact(SafeMessage(code));
            _out.WriteError(
                id,
                WireErrorCodes.KLineRejected,
                string.Create(CultureInfo.InvariantCulture, $"K 線請求被拒(代碼 {code}):{message}。The K-line request was rejected (code {code})."),
                writer =>
                {
                    writer.WriteNumber("skCode", code);
                    writer.WriteString("skMessage", message);
                });
            return;
        }

        string[] lines = await WaitForKLineEndAsync(collector).ConfigureAwait(true);

        _out.WriteResult(id, writer =>
        {
            writer.WriteStartArray("lines");

            foreach (string line in lines)
            {
                writer.WriteStringValue(line);
            }

            writer.WriteEndArray();
        });
    }

    /// <summary>
    /// K 線沒有結束事件:以「最後一筆後 <see cref="ServeTimings.KLineQuiet"/> 沒新資料」或「<see cref="ServeTimings.KLineNoData"/>
    /// 都沒資料」結束。
    /// K-lines have no end event: they end <see cref="ServeTimings.KLineQuiet"/> after the last row, or after
    /// <see cref="ServeTimings.KLineNoData"/> with no row at all.
    /// </summary>
    private async Task<string[]> WaitForKLineEndAsync(KLineCollector collector)
    {
        long started = _time.GetTimestamp();

        while (true)
        {
            await Task.Delay(_timings.KLinePoll, _time).ConfigureAwait(true);

            lock (_eventGate)
            {
                bool done = collector.Lines.Count == 0
                    ? _time.GetElapsedTime(started) >= _timings.KLineNoData
                    : _time.GetElapsedTime(collector.LastRowTimestamp) >= _timings.KLineQuiet;

                if (done)
                {
                    _klineCollector = null;
                    return [.. collector.Lines];
                }
            }
        }
    }

    private static bool TryReadKLineParameters(JsonElement parameters, out string symbol, out short type, out string start, out string end)
    {
        symbol = string.Empty;
        type = 0;
        start = string.Empty;
        end = string.Empty;

        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!parameters.TryGetProperty("symbol", out JsonElement symbolElement)
            || symbolElement.ValueKind != JsonValueKind.String
            || !IsValidSymbol(symbolElement.GetString() ?? string.Empty))
        {
            return false;
        }

        if (!parameters.TryGetProperty("type", out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.Number
            || !typeElement.TryGetInt16(out type)
            || type is < 0 or > 3)
        {
            return false;
        }

        if (!TryReadDate(parameters, "start", out start) || !TryReadDate(parameters, "end", out end))
        {
            return false;
        }

        symbol = symbolElement.GetString()!;
        return true;
    }

    private static bool TryReadDate(JsonElement parameters, string name, out string value)
    {
        value = string.Empty;

        if (!parameters.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string text = element.GetString() ?? string.Empty;

        if (!DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return false;
        }

        value = text;
        return true;
    }

    private void HandleStatus(long id)
    {
        int connected = _source.IsConnected();
        int code = _source.GetQuoteStatus(out int connections, out bool outLimit);

        _out.WriteResult(id, writer =>
        {
            writer.WriteBoolean("connected", connected == 1);
            writer.WriteNumber("connections", connections);
            writer.WriteBoolean("outLimit", outLimit);

            if (code != 0)
            {
                writer.WriteNumber("skCode", code);
            }
        });
    }

    /// <summary>
    /// 「交易所,代碼」:恰好一個逗號、兩邊非空、不含 <c>#</c> 與空白或控制字元。
    /// "Exchange,code": exactly one comma, both sides non-empty, no '#', whitespace or control characters.
    /// </summary>
    internal static bool IsValidSymbol(string symbol)
    {
        int comma = symbol.IndexOf(',', StringComparison.Ordinal);

        if (comma <= 0 || comma == symbol.Length - 1 || symbol.IndexOf(',', comma + 1) >= 0)
        {
            return false;
        }

        foreach (char c in symbol)
        {
            if (c == '#' || char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
