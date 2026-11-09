using System.Runtime.InteropServices;

namespace Ozakboy.TradeKit.Capital.Host.Interop;

/// <summary>
/// 帳務查詢事件接收器:把 COM 事件原樣轉成 .NET 事件,不做任何解析(帳號、身分證字號都在原文裡,由呼叫端遮罩後才能寫出)。
/// 字串類事件一律以 (事件名稱, 原文) 送出,查詢狀態事件以 (事件名稱, 狀態, 訊息) 送出。
/// Account query sink: forwards COM events unchanged. The raw text carries account numbers and the login ID, so the
/// caller must redact before writing anything out.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class OrderEventsSink : ISKOrderLibEvents
{
    /// <summary>帳號一筆(登入帳號, 帳號資訊原文)。</summary>
    public event Action<string, string>? Account;

    /// <summary>查詢結果一則(事件名稱, 原文)。</summary>
    public event Action<string, string>? Data;

    /// <summary>查詢狀態(事件名稱, 狀態 0 成功 / 1 失敗, 訊息)。</summary>
    public event Action<string, int, string>? Status;

    /// <inheritdoc/>
    public void OnAccount(string bstrLogInID, string bstrAccountData) => Account?.Invoke(bstrLogInID ?? string.Empty, bstrAccountData ?? string.Empty);

    /// <inheritdoc/>
    public void OnOpenInterest(string bstrData) => Raise(nameof(OnOpenInterest), bstrData);

    /// <inheritdoc/>
    public void OnOverseaFutureOpenInterest(string bstrData) => Raise(nameof(OnOverseaFutureOpenInterest), bstrData);

    /// <inheritdoc/>
    public void OnFutureRights(string bstrData) => Raise(nameof(OnFutureRights), bstrData);

    /// <inheritdoc/>
    public void OnOverSeaFutureRight(string bstrData) => Raise(nameof(OnOverSeaFutureRight), bstrData);

    /// <inheritdoc/>
    public void OnOFOpenInterestGWReport(string bstrData) => Raise(nameof(OnOFOpenInterestGWReport), bstrData);

    /// <inheritdoc/>
    public void OnOpenInterestGWStatus(int nQueryStatus, string bstrErrorMsg) =>
        Status?.Invoke(nameof(OnOpenInterestGWStatus), nQueryStatus, bstrErrorMsg ?? string.Empty);

    /// <inheritdoc/>
    public void OnOverseaFutureOpenInterestGWStatus(int nQueryStatus, string bstrErrorMsg) =>
        Status?.Invoke(nameof(OnOverseaFutureOpenInterestGWStatus), nQueryStatus, bstrErrorMsg ?? string.Empty);

    /// <inheritdoc/>
    public void OnFutureRightsStatus(int nQueryStatus, string bstrErrorMsg) =>
        Status?.Invoke(nameof(OnFutureRightsStatus), nQueryStatus, bstrErrorMsg ?? string.Empty);

    /// <inheritdoc/>
    public void OnOverseaFutureRightsStatus(int nQueryStatus, string bstrErrorMsg) =>
        Status?.Invoke(nameof(OnOverseaFutureRightsStatus), nQueryStatus, bstrErrorMsg ?? string.Empty);

    /// <inheritdoc/>
    public void OnOpenInterestJson(string bstrData) => Raise(nameof(OnOpenInterestJson), bstrData);

    /// <inheritdoc/>
    public void OnOverseaFutureOpenInterestWithDetails(string bstrData) => Raise(nameof(OnOverseaFutureOpenInterestWithDetails), bstrData);

    /// <inheritdoc/>
    public void OnOpenInterestWithDetails(string bstrData) => Raise(nameof(OnOpenInterestWithDetails), bstrData);

    private void Raise(string name, string? data) => Data?.Invoke(name, data ?? string.Empty);
}
