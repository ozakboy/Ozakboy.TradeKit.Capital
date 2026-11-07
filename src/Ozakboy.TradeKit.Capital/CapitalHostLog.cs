using Microsoft.Extensions.Logging;

namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 群益工作階段的日誌行。事件編號 4000–4099。
/// The log lines of the Capital session. Event ids 4000–4099.
/// </summary>
/// <remarks>
/// <b>這裡的每個字串參數,呼叫端都已經過遮罩。</b>宿主 stderr 一行一行遮罩之後才傳進來;帳密本身不會被傳進任何日誌方法。
/// <b>Every string parameter here has already been masked.</b> The host's stderr is masked line by line before it
/// arrives, and the credentials themselves are never passed to any log method.
/// </remarks>
internal static class CapitalHostLog
{
    private static readonly Action<ILogger, string, Exception?> HostOutputCore =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(4001, "CapitalHostOutput"), "capital-host: {Line}");

    private static readonly Action<ILogger, CapitalHostState, CapitalHostState, Exception?> StateChangedCore =
        LoggerMessage.Define<CapitalHostState, CapitalHostState>(
            LogLevel.Information,
            new EventId(4002, "CapitalStateChanged"),
            "群益工作階段狀態 / Capital session state: {Previous} → {Current}");

    private static readonly Action<ILogger, int, int, double, Exception?> HostExitedCore =
        LoggerMessage.Define<int, int, double>(
            LogLevel.Warning,
            new EventId(4003, "CapitalHostExited"),
            "群益宿主非預期結束 / the Capital host exited unexpectedly。結束碼 {ExitCode} / exit code;第 {Restart} 次重啟 / restart;等待 {DelaySeconds:F1} 秒 / seconds。");

    private static readonly Action<ILogger, string, Exception?> FaultedCore =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(4004, "CapitalFaulted"),
            "群益工作階段停止自動重啟 / the Capital session stops restarting:{Reason}");

    private static readonly Action<ILogger, string, Exception?> ProtocolWarningCore =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4005, "CapitalProtocolWarning"), "群益宿主協定 / Capital host protocol:{Reason}");

    private static readonly Action<ILogger, string, int, Exception?> PriceFractionCore =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(4006, "CapitalPriceFractionUnsupported"),
            CapitalErrorCodes.PriceFractionUnsupported + ":{Symbol} 的分母是 {Denominator},不支援分數報價,照小數位數換算 / the denominator is not 1; fractional quotes are unsupported and prices are scaled by decimals。");

    private static readonly Action<ILogger, string, Exception?> ResubscribeFailedCore =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4007, "CapitalResubscribeFailed"),
            "重啟後重送訂閱失敗 / re-sending the subscriptions after a restart failed:{Code}");

    private static readonly Action<ILogger, string, Exception?> EventDiscardedCore =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4008, "CapitalEventDiscarded"),
            "群益事件讀不懂,略過(同一原因只記一次)/ an unreadable Capital event was skipped (logged once per reason):{Reason}");

    private static readonly Action<ILogger, Exception?> KilledCore =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4009, "CapitalHostKilled"),
            "群益宿主沒有在時限內結束,已終止 / the Capital host did not end in time and was killed。");

    private static readonly Action<ILogger, Exception?> StateHandlerFailedCore =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(4010, "CapitalStateHandlerFailed"),
            "StateChanged 的處理常式擲出例外 / a StateChanged handler threw。");

    private static readonly Action<ILogger, string, int, int, Exception?> DomesticKLineSkippedCore =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Warning,
            new EventId(4011, "CapitalDomesticKLineSkipped"),
            "國內 K 線 {Code} 有 {Skipped} 行讀不懂,已略過(其餘 {Kept} 根照回)/ unreadable domestic K-line rows were skipped, the rest returned。");

    private static readonly Action<ILogger, CapitalDomesticState, CapitalDomesticState, string, Exception?> DomesticStateChangedCore =
        LoggerMessage.Define<CapitalDomesticState, CapitalDomesticState, string>(
            LogLevel.Information,
            new EventId(4012, "CapitalDomesticStateChanged"),
            "群益國內報價狀態 / Capital domestic quote state: {Previous} → {Current}({Reason})");

    private static readonly Action<ILogger, Exception?> DomesticStateHandlerFailedCore =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(4013, "CapitalDomesticStateHandlerFailed"),
            "DomesticStateChanged 的處理常式擲出例外 / a DomesticStateChanged handler threw。");

    private static readonly Action<ILogger, string, Exception?> JobUnavailableCore =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4014, "CapitalHostJobUnavailable"),
            "宿主沒有放進 Job Object,父行程被強制結束時改靠宿主自己的 stdin EOF 與父行程看門狗結束 / the host is not in a Job Object; it relies on its own stdin EOF and parent watchdog:{Problem}");

    public static void JobUnavailable(ILogger logger, string problem) => JobUnavailableCore(logger, problem, null);

    public static void DomesticKLineSkipped(ILogger logger, string code, int skipped, int kept) =>
        DomesticKLineSkippedCore(logger, code, skipped, kept, null);

    public static void DomesticStateChanged(ILogger logger, CapitalDomesticState previous, CapitalDomesticState current, string? reason) =>
        DomesticStateChangedCore(logger, previous, current, reason ?? "-", null);

    public static void DomesticStateHandlerFailed(ILogger logger, Exception exception) => DomesticStateHandlerFailedCore(logger, exception);

    public static void HostOutput(ILogger logger, string line) => HostOutputCore(logger, line, null);

    public static void StateChanged(ILogger logger, CapitalHostState previous, CapitalHostState current) =>
        StateChangedCore(logger, previous, current, null);

    public static void HostExited(ILogger logger, int exitCode, int restart, TimeSpan delay) =>
        HostExitedCore(logger, exitCode, restart, delay.TotalSeconds, null);

    public static void Faulted(ILogger logger, string reason) => FaultedCore(logger, reason, null);

    public static void ProtocolWarning(ILogger logger, string reason) => ProtocolWarningCore(logger, reason, null);

    public static void PriceFraction(ILogger logger, string symbol, int denominator) => PriceFractionCore(logger, symbol, denominator, null);

    public static void ResubscribeFailed(ILogger logger, string code) => ResubscribeFailedCore(logger, code, null);

    public static void EventDiscarded(ILogger logger, string reason) => EventDiscardedCore(logger, reason, null);

    public static void Killed(ILogger logger) => KilledCore(logger, null);

    public static void StateHandlerFailed(ILogger logger, Exception exception) => StateHandlerFailedCore(logger, exception);
}
