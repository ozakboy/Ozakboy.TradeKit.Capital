using System.Text.Json;
using Ozakboy.TradeKit.Capital.Protocol;

namespace Ozakboy.TradeKit.Capital;

// 0.3.0:海外選擇權報價(SKOOQuoteLib)。狀態與海外、國內各自獨立;V2.13.59 手冊沒有這個物件的章節。2026-11-09 實測過連線代碼(3001)、
// 商品清單格式與結尾、訂閱字串、報價的小數位數與履約價;成交時區未實測。
// 0.3.0: overseas option quotes with their own state; the manual has no chapter for this object. The connect code, the
// product list, the subscription string and quote scaling were measured on 2026-11-09; tick time zones were not.
public sealed partial class CapitalHostSession
{
    private readonly List<CapitalSymbol> _optionSubscriptions = [];
    private readonly List<CapitalSymbol> _optionDepthSubscriptions = [];
    private CapitalOverseasOptionsState _optionsState = CapitalOverseasOptionsState.NotStarted;
    private string? _optionsReason;

    /// <summary>
    /// 海外選擇權狀態改變(在觸發改變的執行緒上同步呼叫;處理常式不要阻塞)。與 <see cref="StateChanged"/>、<see cref="DomesticStateChanged"/> 分開。
    /// Raised synchronously when the overseas option state changes; separate from the other state events.
    /// </summary>
    public event EventHandler<CapitalOverseasOptionsStateChangedEventArgs>? OverseasOptionsStateChanged;

    /// <summary>海外選擇權報價目前的狀態。The current overseas option quote state.</summary>
    public CapitalOverseasOptionsState OverseasOptionsState
    {
        get
        {
            lock (_stateLock)
            {
                return _optionsState;
            }
        }
    }

    /// <summary>海外選擇權不可用或斷線的原因(<see cref="CapitalDomesticReasons"/> 的字串)。Why it is unavailable or disconnected.</summary>
    public string? OverseasOptionsReason
    {
        get
        {
            lock (_stateLock)
            {
                return _optionsReason;
            }
        }
    }

    /// <summary>
    /// 海外選擇權商品清單(<c>SKOOQuoteLib_RequestProducts</c>;實測以「##,##,##,##,##」那筆結尾,宿主等 60 秒)。實測 2026-11-09 有 56,156 筆,
    /// 格式見 <see cref="CapitalOverseasOptionProduct"/>。
    /// The overseas option product list; 56,156 rows ending with a "##" row as measured.
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 商品;失敗:<see cref="CapitalErrorCodes.OverseasOptionsUnavailable"/>、<see cref="CapitalErrorCodes.ProductsRejected"/>、
    /// <see cref="CapitalErrorCodes.ProductsTimeout"/>。The products, or a failure.
    /// </returns>
    public async Task<Result<IReadOnlyList<CapitalOverseasOptionProduct>>> GetOverseasOptionProductsAsync(CancellationToken cancellationToken = default)
    {
        Result<JsonElement> response = await SendAsync("oo-products", null, ProductsTimeout, cancellationToken).ConfigureAwait(false);
        return response.IsFailure ? response.Error : CapitalWire.ParseOptionProducts(response.GetValueOrThrow());
    }

    /// <summary>
    /// 訂海外選擇權報價與成交(<c>RequestStocks</c>,以 # 串接,每檔再 <c>RequestTicks</c>;頁碼 -1,比照海期;實測 2026-11-09 兩檔
    /// <c>CME,ES00100X6#CME,ES00100L6</c> 回 0、報價與五檔 / 十檔都有來)。事件:<see cref="CapitalOverseasOptionQuote"/>、
    /// <see cref="CapitalOverseasOptionTick"/>(成交未實測到)、<see cref="CapitalBest5"/>(<see cref="CapitalQuoteMarket.OverseasOptions"/>)。
    /// 代碼用 <see cref="CapitalOverseasOptionProduct.Symbol"/>。
    /// Subscribes overseas option quotes and ticks; the '#'-joined subscription was measured, ticks were not.
    /// </summary>
    /// <param name="symbols">商品。The symbols.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>宿主目前的完整集合。The host's full set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="symbols"/> 為 null。<paramref name="symbols"/> is null.</exception>
    public Task<Result<IReadOnlyList<CapitalSymbol>>> SubscribeOverseasOptionsAsync(IEnumerable<CapitalSymbol> symbols, CancellationToken cancellationToken = default) =>
        SubscribeOptionsAsync("oo-subscribe", "subscribed", _optionSubscriptions, symbols, cancellationToken);

    /// <summary>
    /// 訂海外選擇權十檔(<c>RequestMarketDepth</c>),事件是 <see cref="CapitalBest10"/>(<see cref="CapitalQuoteMarket.OverseasOptions"/>;
    /// 實測 2026-11-09 有來,格式同海期十檔)。
    /// Subscribes the overseas option best 10 (measured, same format as the futures best 10).
    /// </summary>
    /// <param name="symbols">商品。The symbols.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>宿主目前的完整十檔集合。The host's full best 10 set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="symbols"/> 為 null。<paramref name="symbols"/> is null.</exception>
    public Task<Result<IReadOnlyList<CapitalSymbol>>> SubscribeOverseasOptionDepthAsync(IEnumerable<CapitalSymbol> symbols, CancellationToken cancellationToken = default) =>
        SubscribeOptionsAsync("oo-depth", "depth", _optionDepthSubscriptions, symbols, cancellationToken);

    private async Task<Result<IReadOnlyList<CapitalSymbol>>> SubscribeOptionsAsync(
        string method,
        string resultName,
        List<CapitalSymbol> target,
        IEnumerable<CapitalSymbol> symbols,
        CancellationToken cancellationToken)
    {
        Result<List<CapitalSymbol>> requested = ValidateSymbols(symbols);

        if (requested.IsFailure)
        {
            return requested.Error;
        }

        List<CapitalSymbol> list = requested.GetValueOrThrow();

        // 第一次連線最多 60 秒,宿主等到有結果才回。The first connect can take up to 60 s; the host waits for it.
        Result<JsonElement> response = await SendAsync(method, writer => WriteStrings(writer, "symbols", list.Select(symbol => symbol.ToRequestString())), KLineTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.Error;
        }

        Result<IReadOnlyList<CapitalSymbol>> result = ReadSymbolArray(response.GetValueOrThrow(), resultName);

        if (result.IsSuccess)
        {
            Remember(target, list);
        }

        return result;
    }

    private void ApplyOverseasOptionsStatus(HostStatus status)
    {
        if (status.OverseasOptions is null)
        {
            return;
        }

        if (!CapitalWire.TryParseOverseasOptionsState(status.OverseasOptions, out CapitalOverseasOptionsState state))
        {
            WarnOnce("oo:" + status.OverseasOptions, () => CapitalHostLog.ProtocolWarning(_logger, "不認得的海外選擇權狀態 / unknown overseas option state " + status.OverseasOptions));
            return;
        }

        SetOverseasOptionsState(state, status.OverseasOptionsReason, status.OverseasOptionsCode);
    }

    private void SetOverseasOptionsState(CapitalOverseasOptionsState next, string? reason, int? code)
    {
        CapitalOverseasOptionsState previous;

        lock (_stateLock)
        {
            previous = _optionsState;

            if (previous == next && string.Equals(_optionsReason, reason, StringComparison.Ordinal))
            {
                return;
            }

            _optionsState = next;
            _optionsReason = reason;
        }

        CapitalHostLog.OverseasOptionsStateChanged(_logger, previous, next, reason);

        try
        {
            OverseasOptionsStateChanged?.Invoke(this, new CapitalOverseasOptionsStateChangedEventArgs(previous, next, reason, code));
        }
#pragma warning disable CA1031 // 呼叫端處理常式的缺陷不能拖垮讀取迴圈或監督;記錄後繼續。A handler defect must not stop the reader or the supervisor.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            CapitalHostLog.OverseasOptionsStateHandlerFailed(_logger, exception);
        }
    }
}
