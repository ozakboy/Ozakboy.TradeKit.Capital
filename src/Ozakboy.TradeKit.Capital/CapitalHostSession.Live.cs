using System.Text.Json;
using Ozakboy.TradeKit.Capital.Protocol;

namespace Ozakboy.TradeKit.Capital;

// 0.3.0:SKCOM 版本、國內期貨 / 選擇權即時報價、海外五檔 / 十檔。依手冊 V2.13.59 實作,格式與時間語意 2026-11-09 以 probe-full 實測過
// (見 docs/DESIGN.md §5.3;夜盤跨午夜、10 檔上限、宿主 serve 本身仍未實測)。
// 0.3.0: the SKCOM version, domestic live quotes and the overseas best 5 / best 10; formats and time semantics were
// measured on 2026-11-09 (after midnight, the 10-code cap and serve itself were not).
public sealed partial class CapitalHostSession
{
    private readonly List<string> _domesticSubscriptions = [];
    private readonly List<CapitalSymbol> _depthSubscriptions = [];
    private string? _skcomVersion;

    /// <summary>
    /// 宿主回報的 SKCOM 版本(<c>SKCenterLib_GetSKAPIVersionAndBit</c>,例如 <c>2.13.59_x64</c>);宿主還沒回報或讀不到時為 <see langword="null"/>。
    /// 不是 <see cref="CapitalSkcomVersion.Tested"/> 裡的版本時記一筆 <see cref="CapitalErrorCodes.SkcomVersionUntested"/> 警告日誌(每個版本一次),照常運作。
    /// The SKCOM version the host reported; null until it does. A version outside the tested list logs one warning per
    /// version and everything keeps running.
    /// </summary>
    public string? SkcomVersion
    {
        get
        {
            lock (_stateLock)
            {
                return _skcomVersion;
            }
        }
    }

    /// <summary>
    /// <see cref="SkcomVersion"/> 是否是實測過的版本(<see cref="CapitalSkcomVersion.IsTested"/>);還不知道版本時為 <see langword="false"/>。
    /// Whether SkcomVersion is a measured version; false while unknown.
    /// </summary>
    public bool IsTestedSkcomVersion => CapitalSkcomVersion.IsTested(SkcomVersion);

    /// <summary>目前的國內訂閱(重啟後重送用)。The current domestic subscriptions, re-sent after a restart.</summary>
    public IReadOnlyList<string> DomesticSubscriptions
    {
        get
        {
            lock (_stateLock)
            {
                return [.. _domesticSubscriptions];
            }
        }
    }

    /// <summary>
    /// 國內商品清單(<c>SKQuoteLib_RequestStockList</c>,手冊 4-4-17;以「##」那則結尾,宿主等 60 秒)。需要群益期貨戶與期貨 API 下單聲明書
    /// (沒有時群益回 3031)。欄位見 <see cref="CapitalDomesticProduct"/>(實測過;每檔都有結尾 AM 的日盤代碼與全盤代碼一對)。
    /// The domestic product list. A futures account and the signed API agreement are required. The fields were measured;
    /// every code comes as a day-only "AM" code and a full-session code.
    /// </summary>
    /// <param name="market">期貨或選擇權。Futures or options.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 商品清單;失敗:<see cref="CapitalErrorCodes.InvalidArgument"/>、<see cref="CapitalErrorCodes.DomesticUnavailable"/>、
    /// <see cref="CapitalErrorCodes.ProductsRejected"/>、<see cref="CapitalErrorCodes.ProductsTimeout"/>。The products, or a failure.
    /// </returns>
    public async Task<Result<IReadOnlyList<CapitalDomesticProduct>>> GetDomesticProductsAsync(CapitalDomesticMarket market, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(market))
        {
            return CapitalErrors.InvalidArgument("國內市場別只能是 Futures / Options。The domestic market must be Futures or Options.");
        }

        Result<JsonElement> response = await SendAsync("tw-products", writer => writer.WriteNumber("market", (int)market), ProductsTimeout, cancellationToken)
            .ConfigureAwait(false);

        return response.IsFailure ? response.Error : CapitalWire.ParseDomesticProducts(response.GetValueOrThrow(), market);
    }

    /// <summary>
    /// 訂國內報價、成交、五檔與即時分 K(與已訂集合取聯集;手冊 4-4-2 / 4-4-3)。事件:<see cref="CapitalDomesticQuote"/>、
    /// <see cref="CapitalDomesticTick"/>(訂閱當下先回補當日成交)、<see cref="CapitalBest5"/>(<see cref="CapitalQuoteMarket.Domestic"/>)、
    /// <see cref="CapitalDomesticLiveBar"/>。整組最多 <c>10</c> 檔(手冊 4-4-1;超過回 <see cref="CapitalErrorCodes.SubscribeLimitExceeded"/>,不送出)。
    /// 宿主記住集合,國內斷線重連後整組重送;宿主重啟後本工作階段再送一次。事件格式與時間語意 2026-11-09 實測過(見各事件型別);
    /// 要夜盤行情請訂沒有 <c>AM</c> 結尾的全盤代碼(<c>TX00</c>,不是 <c>TX00AM</c>)。10 檔上限是手冊值,實測只訂了 4 檔。
    /// Subscribes domestic quotes, ticks, the best 5 and live bars, merged with the current set; at most 10 codes. The host
    /// re-sends the whole set after a domestic reconnect and this session re-sends it after a host restart. The event
    /// formats were measured; subscribe the full-session code (no "AM" suffix) for the night session.
    /// </summary>
    /// <param name="codes">群益國內代碼(例如 <c>TX00</c>)。Capital's domestic codes.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>宿主目前的完整國內訂閱集合;失敗見 <see cref="CapitalErrorCodes"/>。The host's full domestic set, or a failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="codes"/> 為 null。<paramref name="codes"/> is null.</exception>
    public async Task<Result<IReadOnlyList<string>>> SubscribeDomesticAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codes);

        List<string> requested = [];

        foreach (string code in codes)
        {
            if (!IsValidDomesticCode(code))
            {
                return CapitalErrors.InvalidArgument("國內商品代碼不合法(不可空白,不可含逗號、# 或空白字元)。A domestic code is invalid.");
            }

            if (!requested.Contains(code, StringComparer.Ordinal))
            {
                requested.Add(code);
            }
        }

        if (requested.Count == 0)
        {
            return CapitalErrors.InvalidArgument("至少要一個代碼。At least one code is required.");
        }

        Result<JsonElement> response = await SendAsync("tw-subscribe", writer => WriteStrings(writer, "codes", requested), ShortRequestTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.Error;
        }

        if (!CapitalWire.TryReadStrings(response.GetValueOrThrow(), "subscribed", out List<string> subscribed))
        {
            return CapitalErrors.Protocol("tw-subscribe 的回應沒有 subscribed 陣列。The tw-subscribe result has no subscribed array.");
        }

        lock (_stateLock)
        {
            foreach (string code in requested.Where(code => !_domesticSubscriptions.Contains(code, StringComparer.Ordinal)))
            {
                _domesticSubscriptions.Add(code);
            }
        }

        return subscribed;
    }

    /// <summary>
    /// 訂海外十檔(<c>SKOSQuoteLib_RequestMarketDepth</c>,頁碼 -1),事件是 <see cref="CapitalBest10"/>(<see cref="CapitalQuoteMarket.Overseas"/>)。
    /// 海外五檔(<see cref="CapitalBest5"/>)推定隨 <see cref="SubscribeAsync"/> 的成交訂閱一起來(實測時兩個是連著送的,分不出由哪個觸發)。
    /// 宿主重連後重送。十檔格式 2026-11-09 實測過,見 <see cref="CapitalBest10"/>。
    /// Subscribes the overseas best 10 (format measured); the best 5 is assumed to come with the tick subscription of
    /// SubscribeAsync, which the measurement could not separate from market depth.
    /// </summary>
    /// <param name="symbols">商品。The symbols.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>宿主目前的完整十檔集合。The host's full best 10 set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="symbols"/> 為 null。<paramref name="symbols"/> is null.</exception>
    public async Task<Result<IReadOnlyList<CapitalSymbol>>> SubscribeDepthAsync(IEnumerable<CapitalSymbol> symbols, CancellationToken cancellationToken = default)
    {
        Result<List<CapitalSymbol>> requested = ValidateSymbols(symbols);

        if (requested.IsFailure)
        {
            return requested.Error;
        }

        List<CapitalSymbol> list = requested.GetValueOrThrow();
        Result<JsonElement> response = await SendAsync("depth", writer => WriteStrings(writer, "symbols", list.Select(symbol => symbol.ToRequestString())), ShortRequestTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.Error;
        }

        Result<IReadOnlyList<CapitalSymbol>> result = ReadSymbolArray(response.GetValueOrThrow(), "depth");

        if (result.IsSuccess)
        {
            Remember(_depthSubscriptions, list);
        }

        return result;
    }

    private static Result<List<CapitalSymbol>> ValidateSymbols(IEnumerable<CapitalSymbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        List<CapitalSymbol> requested = [];

        foreach (CapitalSymbol symbol in symbols)
        {
            if (!symbol.IsValid)
            {
                return CapitalErrors.InvalidArgument("商品代碼不合法(交易所與代碼都要有值,不可含逗號、# 或空白)。A symbol is invalid.");
            }

            if (!requested.Contains(symbol))
            {
                requested.Add(symbol);
            }
        }

        return requested.Count == 0 ? CapitalErrors.InvalidArgument("至少要一個商品。At least one symbol is required.") : requested;
    }

    private static Result<IReadOnlyList<CapitalSymbol>> ReadSymbolArray(JsonElement result, string name)
    {
        if (!CapitalWire.TryReadStrings(result, name, out List<string> values))
        {
            return CapitalErrors.Protocol($"回應沒有 {name} 陣列。The result has no {name} array.");
        }

        List<CapitalSymbol> symbols = [];

        foreach (string value in values)
        {
            if (CapitalSymbol.TryParse(value, out CapitalSymbol symbol))
            {
                symbols.Add(symbol);
            }
        }

        return symbols;
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);

        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private void Remember(List<CapitalSymbol> target, IEnumerable<CapitalSymbol> symbols)
    {
        lock (_stateLock)
        {
            foreach (CapitalSymbol symbol in symbols.Where(symbol => !target.Contains(symbol)))
            {
                target.Add(symbol);
            }
        }
    }

    /// <summary>
    /// 宿主重啟後重送 0.3.0 的訂閱(十檔、國內、海外選擇權)。國內與海外選擇權在首次連線時由宿主等著,所以直接送。
    /// Re-sends the 0.3.0 subscriptions after a host restart.
    /// </summary>
    private async Task ResubscribeLiveAsync(CapitalHostProcess host, CancellationToken cancellationToken)
    {
        string[] depth;
        string[] domestic;
        string[] options;
        string[] optionDepth;

        lock (_stateLock)
        {
            depth = [.. _depthSubscriptions.Select(symbol => symbol.ToRequestString())];
            domestic = [.. _domesticSubscriptions];
            options = [.. _optionSubscriptions.Select(symbol => symbol.ToRequestString())];
            optionDepth = [.. _optionDepthSubscriptions.Select(symbol => symbol.ToRequestString())];
        }

        foreach ((string method, string name, string[] values) in new[]
                 {
                     ("depth", "symbols", depth),
                     ("tw-subscribe", "codes", domestic),
                     ("oo-subscribe", "symbols", options),
                     ("oo-depth", "symbols", optionDepth),
                 })
        {
            if (values.Length == 0)
            {
                continue;
            }

            // 國內與海外選擇權第一次連線最多 60 秒,宿主等到有結果才回。The first domestic connect can take up to 60 s.
            Result<JsonElement> response = await host.RequestAsync(method, writer => WriteStrings(writer, name, values), KLineTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsFailure)
            {
                CapitalHostLog.ResubscribeFailed(_logger, method + ":" + response.Error.Code);
            }
        }
    }

    private void ClearLiveSubscriptions()
    {
        _domesticSubscriptions.Clear();
        _depthSubscriptions.Clear();
        _optionSubscriptions.Clear();
        _optionDepthSubscriptions.Clear();
    }

    private void ApplySkcomVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        string trimmed = version.Trim();

        lock (_stateLock)
        {
            if (string.Equals(_skcomVersion, trimmed, StringComparison.Ordinal))
            {
                return;
            }

            _skcomVersion = trimmed;
        }

        CapitalHostLog.SkcomVersion(_logger, trimmed);

        if (!CapitalSkcomVersion.IsTested(trimmed))
        {
            WarnOnce("skcom-version:" + trimmed, () => CapitalHostLog.SkcomVersionUntested(_logger, trimmed));
        }
    }

    /// <summary>0.3.0 的事件;認得就寫進佇列並回 <see langword="true"/>。Handles a 0.3.0 event; true when known.</summary>
    private bool DispatchLiveEvent(string name, JsonElement data)
    {
        DateTimeOffset now = _time.GetUtcNow();
        CapitalMarketEvent? item;
        string problem;

        switch (name)
        {
            case "tw-quote":
                item = CapitalWire.TryParseDomesticQuote(data, now, out CapitalDomesticQuote? quote, out problem) ? quote : null;
                break;
            case "tw-tick":
                item = CapitalWire.TryParseDomesticTick(data, out CapitalDomesticTick? tick, out problem) ? tick : null;
                break;
            case "tw-live-bar":
                item = CapitalWire.TryParseDomesticLiveBar(data, out CapitalDomesticLiveBar? bar, out problem) ? bar : null;
                break;
            case "tw-best5" or "best5" or "oo-best5":
                item = CapitalWire.TryParseBest5(data, MarketOf(name), now, out CapitalBest5? best5, out problem) ? best5 : null;
                break;
            case "best10" or "oo-best10":
                item = CapitalWire.TryParseBest10(data, MarketOf(name), now, out CapitalBest10? best10, out problem) ? best10 : null;
                break;
            case "oo-quote":
                item = CapitalWire.TryParseOptionQuote(data, now, out CapitalOverseasOptionQuote? optionQuote, out problem) ? optionQuote : null;
                break;
            case "oo-tick":
                item = CapitalWire.TryParseOptionTick(data, out CapitalOverseasOptionTick? optionTick, out problem) ? optionTick : null;
                break;
            default:
                return false;
        }

        if (item is null)
        {
            WarnOnce(name + ":" + problem, () => CapitalHostLog.EventDiscarded(_logger, problem));
            return true;
        }

        _ = _events.Writer.TryWrite(item);
        return true;
    }

    private static CapitalQuoteMarket MarketOf(string eventName) =>
        eventName.StartsWith("tw-", StringComparison.Ordinal) ? CapitalQuoteMarket.Domestic
        : eventName.StartsWith("oo-", StringComparison.Ordinal) ? CapitalQuoteMarket.OverseasOptions
        : CapitalQuoteMarket.Overseas;
}
