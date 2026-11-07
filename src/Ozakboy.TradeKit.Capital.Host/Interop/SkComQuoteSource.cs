using System.Runtime.InteropServices;
using Ozakboy.TradeKit.Capital.Host.Serve;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

/// <summary>
/// <see cref="ISkQuoteSource"/> 的 SKCOM 實作。所有方法都必須在建立物件的那條 STA 執行緒上呼叫,事件也從那裡的訊息迴圈送進來。
/// The SKCOM implementation of <see cref="ISkQuoteSource"/>. Every method must be called on the STA thread that created
/// the objects, and events arrive through that thread's message loop.
/// </summary>
/// <remarks>
/// 呼叫順序照探針實測可行的那一套(<c>ProbeRunner</c>):建立物件 → SetLogPath → 掛 OnReplyMessage 與海外事件 → Login →
/// EnterMonitorLONG → 等 OnConnect(3001) → 商品 / 訂閱 / K 線。
/// The call order follows the one the probe measured to work: create the objects, SetLogPath, attach OnReplyMessage and
/// the overseas events, Login, EnterMonitorLONG, wait for OnConnect(3001), then products, subscriptions and K-lines.
/// </remarks>
internal sealed class SkComQuoteSource : ISkQuoteSource
{
    private const int HResultClassNotRegistered = unchecked((int)0x80040154);
    private const int HResultModuleNotFound = unchecked((int)0x8007007E);

    private ISKCenterLib? _center;
    private ISKOSQuoteLib? _quote;

    // SKReplyLib 只用來掛公告事件(登入前必要),本身不呼叫任何方法;仍要留著參考,免得它的 RCW 被 GC 回收、事件跟著失效。
    // SKReplyLib is only there for the announcement event; it is kept referenced so its RCW is never collected.
    private object? _replyLib;
    private ComEventConnection? _replyConnection;
    private ComEventConnection? _quoteConnection;
    private bool _disposed;

    /// <inheritdoc/>
    public event Action<string>? Announcement;

    /// <inheritdoc/>
    public event Action<int>? Connection;

    /// <inheritdoc/>
    public event Action<string>? Product;

    /// <inheritdoc/>
    public event Action<string>? ProductDetail;

    /// <inheritdoc/>
    public event Action<string, string>? KLine;

    /// <inheritdoc/>
    public event Action<int>? Quote;

    /// <inheritdoc/>
    public event Action<SkTickData>? Tick;

    private ISKCenterLib Center => _center ?? throw new InvalidOperationException("SKCOM 尚未建立,先呼叫 Open。");

    private ISKOSQuoteLib OsQuote => _quote ?? throw new InvalidOperationException("SKCOM 尚未建立,先呼叫 Open。");

    /// <inheritdoc/>
    public SkOpenFailure? Open()
    {
        object replyLib;

        try
        {
            _center = (ISKCenterLib)Create(SkComIds.CenterLibClsid);
            replyLib = Create(SkComIds.ReplyLibClsid);
            _replyLib = replyLib;
            _quote = (ISKOSQuoteLib)Create(SkComIds.OsQuoteLibClsid);
        }
        catch (Exception exception) when (exception is COMException or IOException or InvalidCastException or TypeLoadException)
        {
            // 0x80040154 = 沒註冊;0x8007007E = 註冊了但登錄檔指向的 SKCOM.dll(或它相依的 DLL)不存在
            string reason = exception.HResult switch
            {
                HResultClassNotRegistered => ServeStates.ReasonNotRegistered,
                HResultModuleNotFound => ServeStates.ReasonFileMissing,
                _ => ServeStates.ReasonCreateFailed,
            };
            return new SkOpenFailure(reason, exception.HResult);
        }

        ReplyEventsSink replySink = new();
        replySink.MessageReceived += message => Announcement?.Invoke(message);
        _replyConnection = ComEventConnection.Advise(replyLib, SkComIds.ReplyLibEventsIid, replySink);

        OsQuoteEventsSink quoteSink = new();
        quoteSink.Connected += (code, _) => Connection?.Invoke(code);
        quoteSink.Product += value => Product?.Invoke(value);
        quoteSink.ProductDetail += value => ProductDetail?.Invoke(value);
        quoteSink.KLine += (stockNo, data) => KLine?.Invoke(stockNo, data);
        quoteSink.Quote += index => Quote?.Invoke(index);
        quoteSink.Tick += (index, pointer, date, time, close, qty, history) =>
            Tick?.Invoke(new SkTickData(index, pointer, date, time, close, qty, history));
        _quoteConnection = ComEventConnection.Advise(_quote, SkComIds.OsQuoteLibEventsIid, quoteSink);

        return null;
    }

    /// <inheritdoc/>
    public int SetLogPath(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 目錄建不起來就交給 SKCOM 自己回錯誤代碼。If the directory cannot be created, SKCOM reports its own code.
        }

        return Center.SKCenterLib_SetLogPath(path);
    }

    /// <inheritdoc/>
    public int Login(string loginId, string password) => Center.SKCenterLib_Login(loginId, password);

    /// <inheritdoc/>
    public string GetReturnCodeMessage(int code) => Center.SKCenterLib_GetReturnCodeMessage(code) ?? string.Empty;

    /// <inheritdoc/>
    public string GetLastLogInfo() => Center.SKCenterLib_GetLastLogInfo() ?? string.Empty;

    /// <inheritdoc/>
    public int EnterMonitor() => OsQuote.SKOSQuoteLib_EnterMonitorLONG();

    /// <inheritdoc/>
    public int LeaveMonitor() => _quote is null ? 0 : _quote.SKOSQuoteLib_LeaveMonitor();

    /// <inheritdoc/>
    public int IsConnected() => OsQuote.SKOSQuoteLib_IsConnected();

    /// <inheritdoc/>
    public int GetQuoteStatus(out int connections, out bool outLimit) =>
        OsQuote.SKOSQuoteLib_GetQuoteStatus(out connections, out outLimit);

    /// <inheritdoc/>
    public int RequestOverseaProducts() => OsQuote.SKOSQuoteLib_RequestOverseaProducts();

    /// <inheritdoc/>
    public int GetOverseaProductDetail(short type) => OsQuote.SKOSQuoteLib_GetOverseaProductDetail(type);

    /// <inheritdoc/>
    public int RequestStocks(string stockNos)
    {
        short page = -1;
        return OsQuote.SKOSQuoteLib_RequestStocks(ref page, stockNos);
    }

    /// <inheritdoc/>
    public int RequestTicks(string stockNo)
    {
        short page = -1;
        return OsQuote.SKOSQuoteLib_RequestTicks(ref page, stockNo);
    }

    /// <inheritdoc/>
    public int RequestKLineByDate(string stockNo, short type, string startDate, string endDate, short minuteNumber) =>
        OsQuote.SKOSQuoteLib_RequestKLineByDate(stockNo, type, startDate, endDate, minuteNumber);

    /// <inheritdoc/>
    public int GetStockByIndex(int index, out SkQuoteData quote)
    {
        int code = OsQuote.SKOSQuoteLib_GetStockByIndexNineDigitLONG(index, out SKFOREIGN_9LONG item);

        quote = new SkQuoteData
        {
            ExchangeNo = item.bstrExchangeNo ?? string.Empty,
            StockNo = item.bstrStockNo ?? string.Empty,
            StockName = item.bstrStockName ?? string.Empty,
            Decimals = item.sDecimal,
            Denominator = item.nDenominator,
            TradingDay = item.nTradingDay,
            Open = item.nOpen,
            High = item.nHigh,
            Low = item.nLow,
            Close = item.nClose,
            Settle = item.nSettlePrice,
            Reference = item.nRef,
            Bid = item.nBid,
            BidQuantity = item.nBc,
            Ask = item.nAsk,
            AskQuantity = item.nAc,
            TotalQuantity = item.nTQty,
            TickQuantity = item.nTickQty,
        };

        return code;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _quoteConnection?.Dispose();
        _replyConnection?.Dispose();
    }

    private static object Create(Guid clsid)
    {
        Type type = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;
        return Activator.CreateInstance(type)!;
    }
}
