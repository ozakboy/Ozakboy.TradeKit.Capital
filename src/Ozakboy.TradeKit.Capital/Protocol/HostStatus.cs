namespace Ozakboy.TradeKit.Capital.Protocol;

/// <summary>
/// 宿主的 status 事件(<c>{"event":"status","data":{state, reason?, hresult?, code?, message?}}</c>)。
/// A status event from the host.
/// </summary>
/// <param name="State"><c>logging_in</c> / <c>login_failed</c> / <c>connecting</c> / <c>ready</c> / <c>disconnected</c> / <c>faulted</c>。</param>
/// <param name="Reason">faulted 的原因。The faulted reason.</param>
/// <param name="HResult">SKCOM 建立失敗的 HRESULT。The HRESULT when SKCOM could not be created.</param>
/// <param name="Code">群益代碼(登入失敗等)。Capital's code.</param>
/// <param name="Message">群益代碼的說明。Capital's text for the code.</param>
internal sealed record HostStatus(string State, string? Reason, string? HResult, int? Code, string? Message);
