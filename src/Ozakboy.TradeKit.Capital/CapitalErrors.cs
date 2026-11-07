using System.Globalization;
using System.Text.Json;
using Ozakboy.TradeKit.Capital.Protocol;

namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 本套件錯誤的建立方法,統一代碼、分類與訊息格式。訊息一律中英雙語(先中後英),絕不含帳密。
/// Factory methods for this package's failures. Messages are bilingual, Chinese first, and never carry credentials.
/// </summary>
internal static class CapitalErrors
{
    public static Error InvalidOptions(string message) => Error.Validation(CapitalErrorCodes.InvalidOptions, message);

    public static Error InvalidCredentials(string message) => Error.Validation(CapitalErrorCodes.InvalidCredentials, message);

    public static Error InvalidArgument(string message) => Error.Validation(CapitalErrorCodes.InvalidArgument, message);

    public static Error HostNotFound(string path) =>
        new Error(
                CapitalErrorCodes.HostNotFound,
                "找不到群益宿主執行檔,請確認套件的 capital-host 資料夾有複製到輸出目錄。The Capital host executable was not found; check that the package's capital-host folder was copied to the output.",
                ErrorCategory.NotFound)
            .WithData("path", path);

    public static Error HostStartFailed(string reason) =>
        new(
            CapitalErrorCodes.HostStartFailed,
            $"群益宿主行程啟動失敗:{reason} The Capital host process could not be started: {reason}",
            ErrorCategory.Unavailable);

    public static Error HostStartTimeout(TimeSpan timeout) =>
        new(
            CapitalErrorCodes.HostStartTimeout,
            string.Create(
                CultureInfo.InvariantCulture,
                $"群益宿主在 {timeout.TotalSeconds:F0} 秒內沒有就緒,已終止。The Capital host was not ready within {timeout.TotalSeconds:F0} s and was killed."),
            ErrorCategory.Timeout);

    public static Error HostExited(int? exitCode) =>
        new Error(
                CapitalErrorCodes.HostExited,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"群益宿主行程非預期結束(結束碼 {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "?"})。The Capital host process ended unexpectedly (exit code {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "?"})."),
                ErrorCategory.Unavailable)
            .WithData(CapitalErrorDataKeys.ExitCode, exitCode ?? -1);

    public static Error HostNotReady(CapitalHostState state) =>
        new(
            CapitalErrorCodes.HostNotReady,
            $"群益工作階段目前是 {state},不能送請求。The Capital session is {state} and does not accept requests.",
            ErrorCategory.Unavailable);

    public static Error InvalidState(string message) => new(CapitalErrorCodes.InvalidState, message, ErrorCategory.Conflict);

    public static Error Protocol(string message) => new(CapitalErrorCodes.ProtocolError, message, ErrorCategory.Unexpected);

    public static Error LoginFailed(int code, string message)
    {
        string suffix = string.IsNullOrWhiteSpace(message) ? string.Empty : ":" + message;

        return new Error(
                CapitalErrorCodes.LoginFailed,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"群益登入失敗(代碼 {code}){suffix}。帳密類錯誤不會自動重試,錯太多次群益會鎖帳號。Capital's login failed (code {code}); it is never retried automatically, because repeated failures lock the account."),
                ErrorCategory.Unauthorized)
            .WithData(CapitalErrorDataKeys.Code, code)
            .WithData(CapitalErrorDataKeys.Locked, code == 307)
            .WithData(CapitalErrorDataKeys.Message, message ?? string.Empty);
    }

    public static Error CredentialsMissing() =>
        new(
            CapitalErrorCodes.CredentialsMissing,
            "群益宿主沒有收到帳密,沒有登入。The Capital host received no credentials and did not log in.",
            ErrorCategory.Validation);

    /// <summary>宿主回報 faulted 時依 reason 對應錯誤碼。Maps a faulted reason to an error.</summary>
    public static Error Faulted(string reason, string? hresult, int? code, string? message)
    {
        Error error = reason switch
        {
            "skcom_not_registered" => new Error(
                CapitalErrorCodes.SkComNotRegistered,
                "SKCOM 沒有註冊:請從群益官網下載 SKCOM,以系統管理員身分執行 x64 資料夾的 install.bat。SKCOM is not registered: download it from Capital's site and run install.bat of the x64 folder as an administrator.",
                ErrorCategory.Unavailable),
            "skcom_file_missing" => new Error(
                CapitalErrorCodes.SkComFileMissing,
                "SKCOM 已註冊,但登錄檔指向的 DLL 不存在(資料夾被搬走或刪掉),請把 x64 資料夾放到固定位置後重新註冊。SKCOM is registered but its DLL is missing; put the x64 folder in a fixed place and register it again.",
                ErrorCategory.Unavailable),
            "skcom_create_failed" => new Error(
                CapitalErrorCodes.SkComCreateFailed,
                "建立 SKCOM 物件失敗。Creating the SKCOM objects failed.",
                ErrorCategory.Unavailable),
            "connect_timeout" => new Error(
                CapitalErrorCodes.ConnectTimeout,
                "連不上群益海外報價主機(沒有收到連線事件)。The Capital overseas quote server could not be reached.",
                ErrorCategory.Timeout),
            "connect_rejected" => new Error(
                CapitalErrorCodes.ConnectRejected,
                "群益拒絕了海外報價連線請求。Capital rejected the overseas quote connection request.",
                ErrorCategory.Unavailable),
            "missing_credentials" => CredentialsMissing(),
            _ => new Error(
                CapitalErrorCodes.HostInternalError,
                $"群益宿主回報錯誤:{reason}。The Capital host reported a fault: {reason}.",
                ErrorCategory.Unexpected),
        };

        error = error.WithData(CapitalErrorDataKeys.Reason, reason);

        if (!string.IsNullOrEmpty(hresult))
        {
            error = error.WithData(CapitalErrorDataKeys.HResult, hresult);
        }

        if (code is { } value)
        {
            error = error.WithData(CapitalErrorDataKeys.Code, value);
        }

        if (!string.IsNullOrEmpty(message))
        {
            error = error.WithData(CapitalErrorDataKeys.Message, message);
        }

        return error;
    }

    public static Error RequestTimeout(string method, TimeSpan timeout) =>
        new Error(
                CapitalErrorCodes.RequestTimeout,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"群益宿主沒有在 {timeout.TotalSeconds:F0} 秒內回應 {method}。The Capital host did not answer {method} within {timeout.TotalSeconds:F0} s."),
                ErrorCategory.Timeout)
            .WithData(CapitalErrorDataKeys.Method, method);

    public static Error RequestCancelled(string method) =>
        new Error(CapitalErrorCodes.RequestCancelled, "請求已取消。The request was cancelled.", ErrorCategory.Cancelled)
            .WithData(CapitalErrorDataKeys.Method, method);

    public static Error KLineMalformed(string line, string reason) =>
        new Error(
                CapitalErrorCodes.KLineMalformed,
                $"K 線讀不懂:{reason} A K-line row could not be read: {reason}",
                ErrorCategory.Unexpected)
            .WithData("line", line);

    public static Error UnknownExchange(string exchange) =>
        new Error(
                CapitalErrorCodes.UnknownExchange,
                $"交易所 {exchange} 沒有已知時區,時間無法轉成 UTC。The exchange {exchange} has no known time zone, so its times cannot be converted to UTC.",
                ErrorCategory.NotSupported)
            .WithData("exchange", exchange);

    /// <summary>
    /// 宿主回應的錯誤:代碼照用(協定上就是 capital.*),群益代碼轉進 <see cref="CapitalErrorDataKeys.Code"/>。
    /// An error the host answered with: its code is used as is and Capital's code goes into Data["code"].
    /// </summary>
    public static Error FromHost(string method, string code, string message, JsonElement data)
    {
        ErrorCategory category = code switch
        {
            CapitalErrorCodes.ProductsTimeout or CapitalErrorCodes.KLineTimeout => ErrorCategory.Timeout,
            CapitalErrorCodes.NotConnected or CapitalErrorCodes.DomesticUnavailable => ErrorCategory.Unavailable,
            CapitalErrorCodes.RequestInvalid or CapitalErrorCodes.UnknownMethod => ErrorCategory.Unexpected,
            CapitalErrorCodes.KLineRejected or CapitalErrorCodes.SubscribeRejected or CapitalErrorCodes.ProductsRejected => ErrorCategory.Validation,
            _ => ErrorCategory.Unexpected,
        };

        Error error = new Error(code, message, category).WithData(CapitalErrorDataKeys.Method, method);

        if (CapitalWire.GetInt32(data, "skCode") is { } value)
        {
            error = error.WithData(CapitalErrorDataKeys.Code, value);
        }

        foreach ((string wire, string key) in DataFields)
        {
            if (CapitalWire.GetString(data, wire) is { Length: > 0 } text)
            {
                error = error.WithData(key, text);
            }
        }

        if (CapitalWire.GetInt32(data, "received") is { } received)
        {
            error = error.WithData("received", received);
        }

        return error;
    }

    // 宿主錯誤 data 的字串欄位 → Error.Data 的鍵。String members of the host's error data and their Error.Data keys.
    private static readonly (string Wire, string Key)[] DataFields =
    [
        ("skMessage", CapitalErrorDataKeys.Message),
        ("symbol", CapitalErrorDataKeys.Symbol),
        ("reason", CapitalErrorDataKeys.Reason),
        ("domestic", CapitalErrorDataKeys.DomesticState),
    ];
}
