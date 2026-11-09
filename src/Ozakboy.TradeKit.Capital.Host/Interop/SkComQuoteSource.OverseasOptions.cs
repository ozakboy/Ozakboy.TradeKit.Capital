using System.Runtime.InteropServices;
using Ozakboy.TradeKit.Capital.Host.Serve;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

// 0.3.0:海外選擇權報價(SKOOQuoteLib)的 COM 實作。參數比照 SKOSQuoteLib(頁碼 -1),手冊沒有章節,待實測。
// 0.3.0: the COM side of overseas option quotes; parameters follow SKOSQuoteLib (page -1), still to be measured.
internal sealed partial class SkComQuoteSource
{
    private ISKOOQuoteLib? _options;
    private ComEventConnection? _optionsConnection;

    /// <inheritdoc/>
    public event Action<int>? OverseasOptionConnection;

    /// <inheritdoc/>
    public event Action<string>? OverseasOptionProduct;

    /// <inheritdoc/>
    public event Action<int>? OverseasOptionQuote;

    /// <inheritdoc/>
    public event Action<SkTickData>? OverseasOptionTick;

    /// <inheritdoc/>
    public event Action<SkBook>? OverseasOptionBest5;

    /// <inheritdoc/>
    public event Action<SkBook>? OverseasOptionBest10;

    private ISKOOQuoteLib Options => _options ?? throw new InvalidOperationException("海外選擇權報價物件尚未建立,先呼叫 OpenOverseasOptions。");

    /// <inheritdoc/>
    public SkOpenFailure? OpenOverseasOptions()
    {
        try
        {
            _options = (ISKOOQuoteLib)Create(SkComOverseasOptionIds.OoQuoteLibClsid);
        }
        catch (Exception exception) when (exception is COMException or IOException or InvalidCastException or TypeLoadException)
        {
            return new SkOpenFailure(ReasonFor(exception.HResult), exception.HResult);
        }

        OoQuoteEventsSink sink = new();
        sink.Connected += (code, _) => OverseasOptionConnection?.Invoke(code);
        sink.Product += row => OverseasOptionProduct?.Invoke(row);
        sink.Quote += index => OverseasOptionQuote?.Invoke(index);
        sink.Tick += tick => OverseasOptionTick?.Invoke(new SkTickData(tick.StockIdx, tick.Ptr, tick.Date, tick.Time, tick.Close, tick.Qty, tick.History));
        sink.Best5 += book => OverseasOptionBest5?.Invoke(ToBook(book, 5, extended: false));
        sink.Best10 += book => OverseasOptionBest10?.Invoke(ToBook(book, 10, extended: false));
        _optionsConnection = ComEventConnection.Advise(_options, SkComOverseasOptionIds.OoQuoteLibEventsIid, sink);

        return null;
    }

    /// <inheritdoc/>
    public int EnterOverseasOptionMonitor() => Options.SKOOQuoteLib_EnterMonitorLONG();

    /// <inheritdoc/>
    public int LeaveOverseasOptionMonitor() => _options is null ? 0 : _options.SKOOQuoteLib_LeaveMonitor();

    /// <inheritdoc/>
    public int RequestOverseasOptionProducts() => Options.SKOOQuoteLib_RequestProducts();

    /// <inheritdoc/>
    public int RequestOverseasOptionStocks(string stockNos)
    {
        short page = -1;
        return Options.SKOOQuoteLib_RequestStocks(ref page, stockNos);
    }

    /// <inheritdoc/>
    public int RequestOverseasOptionTicks(string stockNo)
    {
        short page = -1;
        return Options.SKOOQuoteLib_RequestTicks(ref page, stockNo);
    }

    /// <inheritdoc/>
    public int RequestOverseasOptionMarketDepth(string stockNo)
    {
        short page = -1;
        return Options.SKOOQuoteLib_RequestMarketDepth(ref page, stockNo);
    }

    /// <inheritdoc/>
    public int GetOverseasOptionStockByIndex(int index, out SkOptionQuote quote)
    {
        SKFOREIGNLONG item = default;
        int code = Options.SKOOQuoteLib_GetStockByIndexLONG(index, ref item);

        quote = new SkOptionQuote
        {
            MarketNo = item.bstrMarketNo ?? string.Empty,
            ExchangeNo = item.bstrExchangeNo ?? string.Empty,
            ExchangeName = item.bstrExchangeName ?? string.Empty,
            StockNo = item.bstrStockNo ?? string.Empty,
            StockName = item.bstrStockName ?? string.Empty,
            CallPut = item.bstrCallPut ?? string.Empty,
            Decimals = item.sDecimal,
            Denominator = item.nDenominator,
            Open = item.nOpen,
            High = item.nHigh,
            Low = item.nLow,
            Close = item.nClose,
            Settle = item.nSettlePrice,
            TickQuantity = item.nTickQty,
            Reference = item.nRef,
            Bid = item.nBid,
            BidQuantity = item.nBc,
            Ask = item.nAsk,
            AskQuantity = item.nAc,
            TotalQuantity = item.nTQty,
            StrikePrice = item.nStrikePrice,
            TradingDay = item.nTradingDay,
        };

        return code;
    }

    private static string ReasonFor(int hresult) => hresult switch
    {
        HResultClassNotRegistered => ServeStates.ReasonNotRegistered,
        HResultModuleNotFound => ServeStates.ReasonFileMissing,
        _ => ServeStates.ReasonCreateFailed,
    };
}
