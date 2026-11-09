using System.Runtime.InteropServices;
using Ozakboy.TradeKit.Capital.Host.Serve;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

// 0.3.0:期貨帳務查詢(唯讀)的 COM 實作。只呼叫 ISKOrderLib 上審過的 10 個唯讀真簽章裡的 9 個(GetOFOpenInterestWithDetails 只給探針用);
// 會改變委託、部位或資金的方法在 SkComOrderInterop.cs 都是 Unused 佔位,這裡呼叫不到,也沒有新增任何簽章。
// 0.3.0: the COM side of the read-only account queries. It calls 9 of the 10 reviewed read-only signatures; nothing
// that could change an order, a position or funds is callable, and no signature was added.
internal sealed partial class SkComQuoteSource
{
    private ISKOrderLib? _order;
    private ComEventConnection? _orderConnection;

    /// <inheritdoc/>
    public event Action<string, string>? AccountRow;

    /// <inheritdoc/>
    public event Action<string, string>? AccountReply;

    /// <inheritdoc/>
    public event Action<string, int, string>? AccountStatus;

    private ISKOrderLib Order => _order ?? throw new InvalidOperationException("SKOrderLib 尚未建立,先呼叫 OpenAccounts。");

    /// <inheritdoc/>
    public SkOpenFailure? OpenAccounts()
    {
        try
        {
            _order = (ISKOrderLib)Create(SkComOrderIds.OrderLibClsid);
        }
        catch (Exception exception) when (exception is COMException or IOException or InvalidCastException or TypeLoadException)
        {
            return new SkOpenFailure(ReasonFor(exception.HResult), exception.HResult);
        }

        OrderEventsSink sink = new();
        sink.Account += (loginId, data) => AccountRow?.Invoke(loginId, data);
        sink.Data += (name, data) => AccountReply?.Invoke(name, data);
        sink.Status += (name, status, message) => AccountStatus?.Invoke(name, status, message);
        _orderConnection = ComEventConnection.Advise(_order, SkComOrderIds.OrderLibEventsIid, sink);

        return null;
    }

    /// <inheritdoc/>
    public int InitializeAccounts() => Order.SKOrderLib_Initialize();

    /// <inheritdoc/>
    public int RequestAccounts() => Order.GetUserAccount();

    /// <inheritdoc/>
    public int ReadCertificate(string loginId) => Order.ReadCertByID(loginId);

    /// <inheritdoc/>
    public int RequestFutureRights(string loginId, string account, short coinType) => Order.GetFutureRights(loginId, account, coinType);

    /// <inheritdoc/>
    public int RequestOpenInterest(string loginId, string account, int format) => Order.GetOpenInterestGW(loginId, account, format);

    /// <inheritdoc/>
    public int RequestOverseaFutureRights(string loginId, string account) => Order.GetRequestOverSeaFutureRight(loginId, account);

    /// <inheritdoc/>
    public int RequestOverseaOpenInterest(string loginId, string account, int format) => Order.GetOverseaFutureOpenInterestGW(loginId, account, format);

    /// <inheritdoc/>
    public string GetOrderReport(string loginId, string account, int format) => Order.GetOrderReport(loginId, account, format) ?? string.Empty;

    /// <inheritdoc/>
    public string GetFulfillReport(string loginId, string account, int format) => Order.GetFulfillReport(loginId, account, format) ?? string.Empty;
}
