using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Protocol;

// 0.3.0:國內即時報價、海外五檔 / 十檔、海外選擇權事件與商品清單的解析。價格一律原始整數 ÷ 10^decimals(CapitalPriceScale,不經 double);
// 國內時間當台北時間(UTC+8,台灣無夏令)轉 UTC。格式、欄位位置與時間語意依手冊,2026-11-09 以 probe-full 實測核對過(各方法註明)。
// 0.3.0: parsing of the domestic live events, the overseas best 5 / best 10, the overseas option events and their
// product lists. Prices are raw integers divided by 10^decimals; domestic times are read as Taipei time.
internal static partial class CapitalWire
{
    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);

    /// <summary>海外選擇權狀態的協定字串(與國內同一組)。Maps the overseas option state's wire string.</summary>
    /// <param name="wire">協定字串。The wire string.</param>
    /// <param name="state">狀態。The state.</param>
    /// <returns>認得時為 <see langword="true"/>。True when known.</returns>
    public static bool TryParseOverseasOptionsState(string? wire, out CapitalOverseasOptionsState state)
    {
        (bool known, state) = wire switch
        {
            "connecting" => (true, CapitalOverseasOptionsState.Connecting),
            "ready" => (true, CapitalOverseasOptionsState.Ready),
            "disconnected" => (true, CapitalOverseasOptionsState.Disconnected),
            "unavailable" => (true, CapitalOverseasOptionsState.Unavailable),
            _ => (false, CapitalOverseasOptionsState.NotStarted),
        };

        return known;
    }

    /// <summary>
    /// 台北時間的 yyyyMMdd + HHmmss(+ 微秒)→ UTC;讀不懂為 null。<paramref name="micros"/> 是群益的 nTimemillismicros(前三位毫秒、後三位微秒,
    /// 手冊 5-6),不在 0–999999 就不加。
    /// Taipei yyyyMMdd and HHmmss, plus microseconds, to UTC; null when unreadable.
    /// </summary>
    /// <param name="date">yyyyMMdd。</param>
    /// <param name="hhmmss">HHmmss。</param>
    /// <param name="micros">毫秒微秒。Milliseconds and microseconds.</param>
    /// <returns>UTC 時間或 null。The UTC time or null.</returns>
    public static DateTimeOffset? TaipeiToUtc(int date, int hhmmss, int micros = 0)
    {
        string text = date.ToString("D8", CultureInfo.InvariantCulture) + hhmmss.ToString("D6", CultureInfo.InvariantCulture);

        if (!DateTime.TryParseExact(text, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            return null;
        }

        DateTimeOffset utc = new DateTimeOffset(local, TaipeiOffset).ToUniversalTime();
        return micros is > 0 and <= 999_999 ? utc.AddMicroseconds(micros) : utc;
    }

    /// <summary>解析 <c>tw-quote</c>。Parses a tw-quote event.</summary>
    public static bool TryParseDomesticQuote(JsonElement data, DateTimeOffset receivedUtc, [NotNullWhen(true)] out CapitalDomesticQuote? quote, out string problem)
    {
        quote = null;

        if (GetString(data, "code") is not { Length: > 0 } code || !TryDecimals(data, out int decimals))
        {
            problem = "tw-quote 缺 code 或 decimals 不在 0–9。The tw-quote lacks code or valid decimals.";
            return false;
        }

        int dealTime = GetInt32(data, "dealTime") ?? -1;
        decimal? close = NonZeroPrice(data, "close", decimals);

        // 實測 2026-11-09:訂閱後第一則快照可能只填一部分,其餘是 0(約 10 毫秒內補齊);國內價格不可能是 0,所以 0 → null。
        // Measured: the first snapshot after subscribing may be partly zero; a domestic price cannot be zero, so zero is null.
        quote = new CapitalDomesticQuote
        {
            Symbol = new CapitalSymbol(CapitalSymbol.DomesticExchange, code),
            Code = code,
            Market = (CapitalDomesticMarket)(GetInt32(data, "market") ?? 0),
            Name = GetString(data, "name") ?? string.Empty,
            Decimals = decimals,
            Open = NonZeroPrice(data, "open", decimals),
            High = NonZeroPrice(data, "high", decimals),
            Low = NonZeroPrice(data, "low", decimals),
            Close = close,
            Reference = NonZeroPrice(data, "ref", decimals),
            Bid = NonZeroPrice(data, "bid", decimals),
            BidQuantity = GetInt64(data, "bidQty") ?? 0,
            Ask = NonZeroPrice(data, "ask", decimals),
            AskQuantity = GetInt64(data, "askQty") ?? 0,
            TickQuantity = GetInt64(data, "tickQty") ?? 0,
            TotalQuantity = GetInt64(data, "totalQty") ?? 0,
            YesterdayQuantity = GetInt64(data, "yesterdayQty") ?? 0,
            BuyVolume = GetInt64(data, "buyQty") ?? 0,
            SellVolume = GetInt64(data, "sellQty") ?? 0,
            OpenInterest = GetInt64(data, "openInterest") ?? 0,
            LimitUp = NonZeroPrice(data, "up", decimals),
            LimitDown = NonZeroPrice(data, "down", decimals),
            IsSimulated = GetInt32(data, "simulate") == 1,
            TradingDay = Day(GetInt32(data, "tradingDay")),
            DealTime = (dealTime is > 0 and <= 235959 || (dealTime == 0 && close is not null))
                && TimeOnly.TryParseExact(dealTime.ToString("D6", CultureInfo.InvariantCulture), "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time)
                ? time
                : null,
            ReceivedUtc = receivedUtc,
        };

        problem = string.Empty;
        return true;
    }

    /// <summary>解析 <c>tw-tick</c>。Parses a tw-tick event.</summary>
    public static bool TryParseDomesticTick(JsonElement data, [NotNullWhen(true)] out CapitalDomesticTick? tick, out string problem)
    {
        tick = null;

        if (GetString(data, "code") is not { Length: > 0 } code
            || !TryDecimals(data, out int decimals)
            || GetInt64(data, "ptr") is not { } pointer
            || GetInt32(data, "date") is not { } date
            || GetInt32(data, "time") is not { } time
            || TaipeiToUtc(date, time, GetInt32(data, "micros") ?? 0) is not { } utc)
        {
            problem = "tw-tick 缺 code / decimals / ptr,或時間讀不懂。The tw-tick lacks code, decimals or ptr, or its time is unreadable.";
            return false;
        }

        tick = new CapitalDomesticTick
        {
            Symbol = new CapitalSymbol(CapitalSymbol.DomesticExchange, code),
            Code = code,
            Market = (CapitalDomesticMarket)(GetInt32(data, "market") ?? 0),
            Sequence = pointer,
            TimeUtc = utc,
            Date = date,
            Time = time,
            Micros = GetInt32(data, "micros") ?? 0,

            // 實測:試撮的回補列可能整筆是 0(選擇權 08:30:50 那筆)、開盤前的買賣價常是 0;0 → null。Zeros are null, as measured.
            Price = NonZeroPrice(data, "close", decimals),
            Bid = NonZeroPrice(data, "bid", decimals),
            Ask = NonZeroPrice(data, "ask", decimals),
            Quantity = GetInt64(data, "qty") ?? 0,
            IsSimulated = GetInt32(data, "simulate") == 1,
            IsReplay = data.TryGetProperty("history", out JsonElement history) && history.ValueKind == JsonValueKind.True,
        };

        problem = string.Empty;
        return true;
    }

    /// <summary>
    /// 解析 <c>tw-live-bar</c>;小數位數沒帶或重置時價格為 null。實測 2026-11-09:<c>time</c>(HHmm)是那一分鐘的<b>結束</b>時刻
    /// (夜盤第一根是 1501、開盤價 = 15:00:00.003 第一筆成交;20:11:07 的即時更新標 2012),與歷史分 K 相同,所以開始時刻 = 結束 − 1 分鐘。
    /// Parses a tw-live-bar event. The HHmm is the end of the minute as measured, so the open time is one minute earlier.
    /// </summary>
    public static bool TryParseDomesticLiveBar(JsonElement data, [NotNullWhen(true)] out CapitalDomesticLiveBar? bar, out string problem)
    {
        bar = null;

        int? type = GetInt32(data, "type");

        if (GetString(data, "code") is not { Length: > 0 } code || type is not (0 or 1 or 2))
        {
            problem = "tw-live-bar 缺 code 或 type 不是 0–2。The tw-live-bar lacks code or a type of 0-2.";
            return false;
        }

        CapitalLiveBarKind kind = (CapitalLiveBarKind)type.Value;
        int? decimals = GetInt32(data, "decimals") is { } value && value is >= 0 and <= CapitalPriceScale.MaxDecimals ? value : null;
        bool priced = kind != CapitalLiveBarKind.Reset && decimals is not null;
        int date = GetInt32(data, "date") ?? 0;
        int timeHm = GetInt32(data, "time") ?? 0;

        bar = new CapitalDomesticLiveBar
        {
            Symbol = new CapitalSymbol(CapitalSymbol.DomesticExchange, code),
            Code = code,
            Kind = kind,
            Date = date,
            TimeHm = timeHm,
            OpenTimeUtc = kind == CapitalLiveBarKind.Reset || timeHm is < 0 or > 2359 ? null : TaipeiToUtc(date, timeHm * 100)?.AddMinutes(-1),
            Open = priced ? Price(data, "open", decimals!.Value) : null,
            High = priced ? Price(data, "high", decimals!.Value) : null,
            Low = priced ? Price(data, "low", decimals!.Value) : null,
            Close = priced ? Price(data, "close", decimals!.Value) : null,
            Volume = kind == CapitalLiveBarKind.Reset ? null : GetInt64(data, "qty"),
        };

        problem = string.Empty;
        return true;
    }

    /// <summary>
    /// 解析五檔(<c>tw-best5</c> / <c>best5</c> / <c>oo-best5</c>)。國內以 <c>code</c> 識別,海外以 <c>symbol</c>。
    /// Parses a best 5 event; domestic events carry a code, overseas ones a symbol.
    /// </summary>
    public static bool TryParseBest5(JsonElement data, CapitalQuoteMarket market, DateTimeOffset receivedUtc, [NotNullWhen(true)] out CapitalBest5? book, out string problem)
    {
        book = null;

        if (!TryBook(data, market, 5, out CapitalSymbol symbol, out int decimals, out CapitalBookLevel[] bids, out CapitalBookLevel[] asks, out problem))
        {
            return false;
        }

        bool domestic = market == CapitalQuoteMarket.Domestic;

        book = new CapitalBest5
        {
            Symbol = symbol,
            Market = market,
            Decimals = decimals,
            Bids = bids,
            Asks = asks,
            ExtendedBid = domestic && GetInt64(data, "extendBid") is { } extendBid ? new CapitalBookLevel(CapitalPriceScale.FromRaw(extendBid, decimals), GetInt64(data, "extendBidQty") ?? 0) : null,
            ExtendedAsk = domestic && GetInt64(data, "extendAsk") is { } extendAsk ? new CapitalBookLevel(CapitalPriceScale.FromRaw(extendAsk, decimals), GetInt64(data, "extendAskQty") ?? 0) : null,
            IsSimulated = GetInt32(data, "simulate") is { } simulate ? simulate == 1 : null,
            ReceivedUtc = receivedUtc,
        };

        return true;
    }

    /// <summary>解析十檔(<c>best10</c> / <c>oo-best10</c>)。Parses a best 10 event.</summary>
    public static bool TryParseBest10(JsonElement data, CapitalQuoteMarket market, DateTimeOffset receivedUtc, [NotNullWhen(true)] out CapitalBest10? book, out string problem)
    {
        book = null;

        if (!TryBook(data, market, 10, out CapitalSymbol symbol, out int decimals, out CapitalBookLevel[] bids, out CapitalBookLevel[] asks, out problem))
        {
            return false;
        }

        book = new CapitalBest10 { Symbol = symbol, Market = market, Decimals = decimals, Bids = bids, Asks = asks, ReceivedUtc = receivedUtc };
        return true;
    }

    /// <summary>解析 <c>oo-quote</c>。Parses an oo-quote event.</summary>
    public static bool TryParseOptionQuote(JsonElement data, DateTimeOffset receivedUtc, [NotNullWhen(true)] out CapitalOverseasOptionQuote? quote, out string problem)
    {
        quote = null;

        if (!CapitalSymbol.TryParse(GetString(data, "symbol"), out CapitalSymbol symbol) || !TryDecimals(data, out int decimals))
        {
            problem = "oo-quote 的 symbol 或 decimals 讀不懂。The oo-quote's symbol or decimals could not be read.";
            return false;
        }

        quote = new CapitalOverseasOptionQuote
        {
            Symbol = symbol,
            Name = GetString(data, "name") ?? string.Empty,
            ExchangeName = GetString(data, "exchangeName") ?? string.Empty,
            CallPut = GetString(data, "callPut") ?? string.Empty,
            Decimals = decimals,
            TradingDay = Day(GetInt32(data, "tradingDay")),
            Open = NonZeroPrice(data, "open", decimals),
            High = NonZeroPrice(data, "high", decimals),
            Low = NonZeroPrice(data, "low", decimals),
            Close = NonZeroPrice(data, "close", decimals),
            Settle = NonZeroPrice(data, "settle", decimals),
            Reference = NonZeroPrice(data, "ref", decimals),
            Bid = NonZeroPrice(data, "bid", decimals),
            BidQuantity = GetInt64(data, "bidQty") ?? 0,
            Ask = NonZeroPrice(data, "ask", decimals),
            AskQuantity = GetInt64(data, "askQty") ?? 0,
            TotalQuantity = GetInt64(data, "totalQty") ?? 0,
            StrikePriceRaw = GetInt64(data, "strike") ?? 0,
            ReceivedUtc = receivedUtc,
        };

        problem = string.Empty;
        return true;
    }

    /// <summary>解析 <c>oo-tick</c>:交易所時區不明時 TimeUtc 為 null(不丟事件)。Parses an oo-tick; an unknown zone leaves TimeUtc null.</summary>
    public static bool TryParseOptionTick(JsonElement data, [NotNullWhen(true)] out CapitalOverseasOptionTick? tick, out string problem)
    {
        tick = null;

        if (!CapitalSymbol.TryParse(GetString(data, "symbol"), out CapitalSymbol symbol)
            || !TryDecimals(data, out int decimals)
            || GetInt64(data, "price") is not { } price
            || GetInt64(data, "ptr") is not { } pointer)
        {
            problem = "oo-tick 缺 symbol / decimals / price / ptr。The oo-tick lacks symbol, decimals, price or ptr.";
            return false;
        }

        string date = GetString(data, "date") ?? string.Empty;
        string time = GetString(data, "time") ?? string.Empty;
        bool known = CapitalExchangeClock.TryGetTimeZone(symbol.Exchange, out CapitalExchangeTimeZone? zone);
        Result<DateTimeOffset> utc = known ? CapitalExchangeClock.TickToUtc(symbol.Exchange, date, time) : CapitalErrors.UnknownExchange(symbol.Exchange);

        tick = new CapitalOverseasOptionTick
        {
            Symbol = symbol,
            Sequence = pointer,
            TimeUtc = utc.IsSuccess ? utc.GetValueOrThrow() : null,
            IsTimeZoneVerified = utc.IsSuccess && zone!.IsVerified,
            Date = date,
            Time = time,
            Price = CapitalPriceScale.FromRaw(price, decimals),
            Quantity = GetInt64(data, "qty") ?? 0,
            IsReplay = data.TryGetProperty("history", out JsonElement history) && history.ValueKind == JsonValueKind.True,
        };

        problem = string.Empty;
        return true;
    }

    /// <summary>
    /// 解析 <c>tw-products</c> 的 rows(手冊 4-4-h:代碼, 名稱, 最後交易日, 交易所商品代碼, 跳動點, 幣別)。實測 2026-11-09:期貨 6,341 筆、
    /// 選擇權 13,424 筆都是這 6 欄;選擇權另有 120 筆 5 欄的系列列(例如 <c>TXO11,台選11,0,TXO11,</c>:最後交易日 0、跳動點空白、沒有幣別欄),
    /// 所以欄位位置固定,少的欄位就是 null(手冊上市例子那種「少交易所代碼的 5 欄」是證券,本套件不查證券)。
    /// Parses the rows of tw-products. Measured: every futures and option row has the six manual fields at fixed
    /// positions, and 120 option series rows have five (no currency); missing fields are null.
    /// </summary>
    public static Result<IReadOnlyList<CapitalDomesticProduct>> ParseDomesticProducts(JsonElement result, CapitalDomesticMarket market)
    {
        if (!TryReadStrings(result, "rows", out List<string> rows))
        {
            return CapitalErrors.Protocol("tw-products 的回應沒有 rows 陣列。The tw-products result has no rows array.");
        }

        List<CapitalDomesticProduct> products = new(rows.Count);

        foreach (string row in rows)
        {
            string[] fields = CapitalAccountParser.Fields(row);

            products.Add(new CapitalDomesticProduct
            {
                Market = market,
                Code = CapitalAccountParser.Text(fields, 0),
                Name = CapitalAccountParser.Text(fields, 1),
                LastTradeDate = CapitalAccountParser.Date(fields, 2),
                ExchangeCode = CapitalAccountParser.Text(fields, 3),

                // 跳動點文字用「/」分段,實測沒有逗號;超過 6 欄時把中間的欄位併回去,幣別固定是最後一欄。
                // The tick text uses '/', no commas were seen; extra fields are joined back and the currency stays last.
                TickSize = fields.Length > 6 ? string.Join(',', fields[4..^1]) : CapitalAccountParser.Text(fields, 4),
                Currency = fields.Length >= 6 ? CapitalAccountParser.Text(fields, fields.Length - 1) : null,
                RawFields = fields,
            });
        }

        return products;
    }

    /// <summary>
    /// 解析 <c>oo-products</c> 的 items(實測 2026-11-09:5 欄 <c>交易所,交易所名稱,代碼,名稱,最後交易日</c>)。
    /// Parses the items of oo-products (five fields, measured).
    /// </summary>
    public static Result<IReadOnlyList<CapitalOverseasOptionProduct>> ParseOptionProducts(JsonElement result)
    {
        if (!TryReadStrings(result, "items", out List<string> rows))
        {
            return CapitalErrors.Protocol("oo-products 的回應沒有 items 陣列。The oo-products result has no items array.");
        }

        List<CapitalOverseasOptionProduct> products = new(rows.Count);

        foreach (string row in rows)
        {
            string[] fields = CapitalAccountParser.Fields(row);
            string? exchange = CapitalAccountParser.Text(fields, 0);
            string? code = CapitalAccountParser.Text(fields, 2);
            CapitalSymbol? symbol = exchange is not null && code is not null && new CapitalSymbol(exchange, code) is { IsValid: true } valid ? valid : null;

            products.Add(new CapitalOverseasOptionProduct
            {
                Exchange = exchange,
                ExchangeName = CapitalAccountParser.Text(fields, 1),
                Code = code,
                Name = CapitalAccountParser.Text(fields, 3),
                LastTradeDate = CapitalAccountParser.Date(fields, 4),
                Symbol = symbol,
                RawFields = fields,
            });
        }

        return products;
    }

    /// <summary>讀字串陣列欄位。Reads a string array member.</summary>
    public static bool TryReadStrings(JsonElement result, string name, out List<string> values)
    {
        values = [];

        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(name, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement element in array.EnumerateArray())
        {
            values.Add(element.GetString() ?? string.Empty);
        }

        return true;
    }

    private static bool TryBook(
        JsonElement data,
        CapitalQuoteMarket market,
        int levels,
        out CapitalSymbol symbol,
        out int decimals,
        out CapitalBookLevel[] bids,
        out CapitalBookLevel[] asks,
        out string problem)
    {
        bids = [];
        asks = [];
        decimals = 0;
        symbol = default;

        bool named = market == CapitalQuoteMarket.Domestic
            ? GetString(data, "code") is { Length: > 0 } code && (symbol = new CapitalSymbol(CapitalSymbol.DomesticExchange, code)).IsValid
            : CapitalSymbol.TryParse(GetString(data, "symbol"), out symbol);

        if (!named || !TryDecimals(data, out decimals)
            || !TryLevels(data, "bidPrices", "bidQuantities", levels, decimals, out bids)
            || !TryLevels(data, "askPrices", "askQuantities", levels, decimals, out asks))
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"{levels} 檔資料讀不懂(代碼、小數位數或檔數不對)。A {levels}-level book could not be read.");
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static bool TryLevels(JsonElement data, string prices, string quantities, int levels, int decimals, out CapitalBookLevel[] result)
    {
        result = [];

        if (!data.TryGetProperty(prices, out JsonElement priceArray) || priceArray.ValueKind != JsonValueKind.Array || priceArray.GetArrayLength() != levels
            || !data.TryGetProperty(quantities, out JsonElement quantityArray) || quantityArray.ValueKind != JsonValueKind.Array || quantityArray.GetArrayLength() != levels)
        {
            return false;
        }

        result = new CapitalBookLevel[levels];

        for (int level = 0; level < levels; level++)
        {
            if (priceArray[level].ValueKind != JsonValueKind.Number || quantityArray[level].ValueKind != JsonValueKind.Number
                || !priceArray[level].TryGetInt64(out long price) || !quantityArray[level].TryGetInt64(out long quantity))
            {
                return false;
            }

            result[level] = new CapitalBookLevel(CapitalPriceScale.FromRaw(price, decimals), quantity);
        }

        return true;
    }

    /// <summary>
    /// 原始整數價格 ÷ 10^decimals;沒有這個欄位或是 0 時為 null(實測 0 代表「這則沒有值」,見 <see cref="CapitalDomesticQuote"/>)。
    /// A raw price divided by 10^decimals; null when missing or zero (zero means "no value" as measured).
    /// </summary>
    private static decimal? NonZeroPrice(JsonElement data, string name, int decimals) =>
        GetInt64(data, name) is { } raw && raw != 0 ? CapitalPriceScale.FromRaw(raw, decimals) : null;

    private static bool TryDecimals(JsonElement data, out int decimals)
    {
        decimals = GetInt32(data, "decimals") ?? -1;
        return decimals is >= 0 and <= CapitalPriceScale.MaxDecimals;
    }

    private static DateOnly? Day(int? value) =>
        value is { } day && DateOnly.TryParseExact(day.ToString("D8", CultureInfo.InvariantCulture), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
}
