using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Protocol;

/// <summary>
/// 宿主 stdout 協定訊息的解析(DESIGN §2):status / quote / tick 事件與 products 回應。
/// Parsing of the host's stdout protocol messages: status, quote and tick events and the products result.
/// </summary>
internal static class CapitalWire
{
    /// <summary>
    /// 解析 status 事件的 data。
    /// Parses a status event's data.
    /// </summary>
    /// <param name="data">data 物件。The data object.</param>
    /// <returns>狀態。The status.</returns>
    public static HostStatus ParseStatus(JsonElement data) =>
        new(
            GetString(data, "state") ?? string.Empty,
            GetString(data, "reason"),
            GetString(data, "hresult"),
            GetInt32(data, "code"),
            GetString(data, "message"));

    /// <summary>
    /// 解析 quote 事件:價格原始整數 ÷ 10^decimals。
    /// Parses a quote event: raw integers divided by 10^decimals.
    /// </summary>
    /// <param name="data">data 物件。The data object.</param>
    /// <param name="receivedUtc">收到的時間。When it was received.</param>
    /// <param name="quote">報價。The quote.</param>
    /// <param name="denominator">nDenominator(實測都是 1)。The denominator, measured as 1.</param>
    /// <param name="problem">讀不懂的原因。Why it could not be read.</param>
    /// <returns>讀得懂時為 <see langword="true"/>。True when readable.</returns>
    public static bool TryParseQuote(
        JsonElement data,
        DateTimeOffset receivedUtc,
        [NotNullWhen(true)] out CapitalQuote? quote,
        out int denominator,
        out string problem)
    {
        quote = null;
        denominator = GetInt32(data, "denominator") ?? 1;

        if (!CapitalSymbol.TryParse(GetString(data, "symbol"), out CapitalSymbol symbol))
        {
            problem = "quote 的 symbol 讀不懂。The quote's symbol could not be read.";
            return false;
        }

        if (GetInt32(data, "decimals") is not { } decimals || decimals is < 0 or > CapitalPriceScale.MaxDecimals)
        {
            problem = "quote 的 decimals 不在 0–9。The quote's decimals are not 0-9.";
            return false;
        }

        if (GetInt32(data, "tradingDay") is not { } tradingDay
            || !DateOnly.TryParseExact(tradingDay.ToString("D8", CultureInfo.InvariantCulture), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
        {
            problem = "quote 的 tradingDay 不是 yyyyMMdd。The quote's tradingDay is not yyyyMMdd.";
            return false;
        }

        quote = new CapitalQuote
        {
            Symbol = symbol,
            Name = GetString(data, "name") ?? string.Empty,
            Decimals = decimals,
            TradingDay = day,
            Open = Price(data, "open", decimals),
            High = Price(data, "high", decimals),
            Low = Price(data, "low", decimals),
            Close = Price(data, "close", decimals),
            Settle = Price(data, "settle", decimals),
            Reference = Price(data, "ref", decimals),
            Bid = Price(data, "bid", decimals),
            BidQuantity = GetInt64(data, "bidQty") ?? 0,
            Ask = Price(data, "ask", decimals),
            AskQuantity = GetInt64(data, "askQty") ?? 0,
            TotalQuantity = GetInt64(data, "totalQty") ?? 0,
            ReceivedUtc = receivedUtc,
        };

        problem = string.Empty;
        return true;
    }

    /// <summary>
    /// 解析 tick 事件:價格依事件裡的 decimals 換算,時間依交易所時區轉 UTC。
    /// Parses a tick event: the price is scaled by the event's decimals and the time converted by the exchange's zone.
    /// </summary>
    /// <param name="data">data 物件。The data object.</param>
    /// <param name="tick">成交。The tick.</param>
    /// <param name="error">讀不懂的原因(未知交易所時代碼是 <see cref="CapitalErrorCodes.UnknownExchange"/>)。Why it failed.</param>
    /// <returns>讀得懂時為 <see langword="true"/>。True when readable.</returns>
    public static bool TryParseTick(
        JsonElement data,
        [NotNullWhen(true)] out CapitalTick? tick,
        [NotNullWhen(false)] out Error? error)
    {
        tick = null;

        if (!CapitalSymbol.TryParse(GetString(data, "symbol"), out CapitalSymbol symbol))
        {
            error = CapitalErrors.Protocol("tick 的 symbol 讀不懂。The tick's symbol could not be read.");
            return false;
        }

        if (GetInt32(data, "decimals") is not { } decimals || decimals is < 0 or > CapitalPriceScale.MaxDecimals
            || GetInt64(data, "price") is not { } price
            || GetInt64(data, "ptr") is not { } pointer)
        {
            error = CapitalErrors.Protocol("tick 缺 decimals / price / ptr。The tick lacks decimals, price or ptr.");
            return false;
        }

        Result<DateTimeOffset> time = CapitalExchangeClock.TickToUtc(
            symbol.Exchange,
            GetString(data, "date") ?? string.Empty,
            GetString(data, "time") ?? string.Empty);

        if (time.IsFailure)
        {
            error = time.Error;
            return false;
        }

        tick = new CapitalTick
        {
            Symbol = symbol,
            Sequence = pointer,
            TimeUtc = time.GetValueOrThrow(),
            Price = CapitalPriceScale.FromRaw(price, decimals),
            Quantity = GetInt64(data, "qty") ?? 0,
            IsReplay = data.TryGetProperty("history", out JsonElement history) && history.ValueKind == JsonValueKind.True,
        };

        error = null;
        return true;
    }

    /// <summary>
    /// 解析 products 回應的 items。日期:<c>99991231</c> 與 <c>0</c>(以及讀不懂的)為 <see langword="null"/>。
    /// Parses the items of the products result; 99991231, 0 and unreadable dates are null.
    /// </summary>
    /// <param name="result">result 物件。The result object.</param>
    /// <returns>商品清單。The products.</returns>
    public static Result<IReadOnlyList<CapitalProduct>> ParseProducts(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return CapitalErrors.Protocol("products 的回應沒有 items 陣列。The products result has no items array.");
        }

        List<CapitalProduct> products = new(items.GetArrayLength());

        foreach (JsonElement item in items.EnumerateArray())
        {
            string exchange = GetString(item, "exchange") ?? string.Empty;
            string code = GetString(item, "code") ?? string.Empty;
            CapitalSymbol symbol = new(exchange, code);

            if (!symbol.IsValid)
            {
                continue;
            }

            products.Add(new CapitalProduct
            {
                Symbol = symbol,
                ExchangeName = GetString(item, "exchangeName") ?? string.Empty,
                Name = GetString(item, "name") ?? string.Empty,
                Kind = GetInt32(item, "kind"),
                LastTradeDate = ParseProductDate(GetString(item, "lastTradeDate")),
                FirstNoticeDate = ParseProductDate(GetString(item, "firstNoticeDate")),
                OrderExchange = GetString(item, "orderExchange"),
                OrderCode = GetString(item, "orderCode"),
            });
        }

        return products;
    }

    /// <summary>
    /// 商品清單的日期:<c>yyyyMMdd</c>;<c>99991231</c>(不到期)與 <c>0</c>(沒有)為 <see langword="null"/>。
    /// A product date: yyyyMMdd, with 99991231 (never expires) and 0 (none) as null.
    /// </summary>
    /// <param name="text">原始文字。The raw text.</param>
    /// <returns>日期或 null。The date or null.</returns>
    public static DateOnly? ParseProductDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();

        if (trimmed is "0" or "99991231")
        {
            return null;
        }

        return DateOnly.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ? date : null;
    }

    /// <summary>讀字串欄位;缺少或不是字串時為 null。Reads a string member; null when missing or not a string.</summary>
    public static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>讀 32 位元整數欄位。Reads an Int32 member.</summary>
    public static int? GetInt32(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
            ? number
            : null;

    /// <summary>讀 64 位元整數欄位。Reads an Int64 member.</summary>
    public static long? GetInt64(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)
            ? number
            : null;

    private static decimal Price(JsonElement data, string name, int decimals) =>
        CapitalPriceScale.FromRaw(GetInt64(data, name) ?? 0, decimals);
}
