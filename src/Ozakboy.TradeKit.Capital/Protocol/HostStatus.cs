namespace Ozakboy.TradeKit.Capital.Protocol;

/// <summary>
/// 宿主的 status 事件(<c>{"event":"status","data":{state, reason?, hresult?, code?, message?, domestic?, domesticReason?, domesticCode?}}</c>)。
/// A status event from the host.
/// </summary>
/// <param name="State"><c>logging_in</c> / <c>login_failed</c> / <c>connecting</c> / <c>ready</c> / <c>disconnected</c> / <c>faulted</c>(海外)。The overseas state.</param>
/// <param name="Reason">faulted 的原因。The faulted reason.</param>
/// <param name="HResult">SKCOM 建立失敗的 HRESULT。The HRESULT when SKCOM could not be created.</param>
/// <param name="Code">群益代碼(登入失敗等)。Capital's code.</param>
/// <param name="Message">群益代碼的說明。Capital's text for the code.</param>
/// <param name="Domestic">
/// 國內狀態(0.2.0 起;<c>connecting</c> / <c>ready</c> / <c>disconnected</c> / <c>unavailable</c>),國內還沒開始時沒有這個欄位。
/// The domestic state (since 0.2.0); absent until the domestic side has started.
/// </param>
/// <param name="DomesticReason">國內不可用或斷線的原因。Why the domestic side is unavailable or disconnected.</param>
/// <param name="DomesticCode">國內 EnterMonitorLONG 被拒時的群益代碼。Capital's code when the domestic EnterMonitorLONG was rejected.</param>
internal sealed record HostStatus(
    string State,
    string? Reason,
    string? HResult,
    int? Code,
    string? Message,
    string? Domestic = null,
    string? DomesticReason = null,
    int? DomesticCode = null);
