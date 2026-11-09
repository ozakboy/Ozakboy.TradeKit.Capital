using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Ozakboy.TradeKit.Capital.Host.Interop;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>
/// 完整探針(<c>probe-full</c>):對真 SKCOM 實測 0.3.0 要加的功能的格式——A. SKCOM 版本;B. 期貨帳務查詢(唯讀);
/// C. 國內期貨 / 選擇權即時報價;D. 海外五檔 / 十檔;E. 海外選擇權報價。<c>--accounts-only</c> 只跑 A + B。
/// 只讀:<see cref="ISKOrderLib"/> 上會改變委託、部位、資金的方法都是 Unused 佔位,根本呼叫不到。帳密只在記憶體;登入一次,失敗不重試。
/// 寫出的每個事件與原文都經 <see cref="ProbeRedactor"/>:登入帳號 → &lt;ID&gt;、帳號號碼 → &lt;ACCTn&gt;、戶名 → &lt;NAME&gt;。
/// The full probe: measures the formats of the features planned for 0.3.0 against real SKCOM. Read-only: every
/// order-changing method of ISKOrderLib is an uncallable placeholder. Credentials stay in memory; one login, no retry.
/// Everything written goes through the redactor.
/// </summary>
internal sealed partial class FullProbeRunner
{
    private static readonly TimeSpan ConnectLimit = TimeSpan.FromSeconds(60);

    private readonly FullProbeOptions _options;
    private readonly ProbeLog _log;
    private readonly ProbeRedactor _redactor;
    private readonly List<ComEventConnection> _connections = [];
    private readonly HashSet<string> _rawHeaders = new(StringComparer.Ordinal);
    private ISKCenterLib? _center;

    /// <summary>
    /// 建立探針。
    /// Creates the probe.
    /// </summary>
    /// <param name="options">選項。Options.</param>
    /// <param name="log">輸出(必須帶同一個遮罩器)。Output; must carry the same redactor.</param>
    /// <param name="redactor">遮罩器。The redactor.</param>
    public FullProbeRunner(FullProbeOptions options, ProbeLog log, ProbeRedactor redactor)
    {
        _options = options;
        _log = log;
        _redactor = redactor;
    }

    private ISKCenterLib Center => _center ?? throw new InvalidOperationException("SKCenterLib 尚未建立。");

    /// <summary>
    /// 在 STA 執行緒上跑完整個探針流程。
    /// Runs the whole probe on the STA thread.
    /// </summary>
    /// <param name="userId">身分證字號(大寫)。The login ID (upper case).</param>
    /// <param name="password">密碼。The password.</param>
    /// <returns>結束碼:0 成功、3 SKCOM 建不起來、4 登入失敗。Exit code.</returns>
    public async Task<int> RunAsync(string userId, string password)
    {
        _redactor.AddLoginId(userId);

        try
        {
            return await RunCoreAsync(userId, password).ConfigureAwait(true);
        }
        finally
        {
            foreach (ComEventConnection connection in _connections)
            {
                connection.Dispose();
            }
        }
    }

    private static object Create(Guid clsid)
    {
        Type type = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;
        return Activator.CreateInstance(type)!;
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (COMException exception)
        {
            return $"(COMException 0x{exception.HResult:X8})";
        }
    }

    private static string Stamp() => DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>檔名用:代碼裡的逗號、空白等換成底線。File-name safe form of a symbol.</summary>
    private static string FileSafe(string code) =>
        new([.. code.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')]);

    /// <summary>結構的全部欄位(依宣告順序),給 probe.jsonl 用。Every field of a struct in declaration order.</summary>
    private static Dictionary<string, object?> Fields<T>(T value)
        where T : struct
    {
        Dictionary<string, object?> fields = new(StringComparer.Ordinal);

        foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(field => field.MetadataToken))
        {
            fields[field.Name] = field.GetValue(value);
        }

        return fields;
    }

    /// <summary>等條件成立或逾時。Waits for a condition or the limit.</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan limit)
    {
        DateTime started = DateTime.UtcNow;

        while (!condition())
        {
            if (DateTime.UtcNow - started > limit)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>等到「安靜 <paramref name="quiet"/>」(最後一則事件之後沒有新的)或 <paramref name="done"/> 成立。</summary>
    private static async Task WaitQuietAsync(Func<DateTime> lastEventUtc, Func<bool> done, TimeSpan quiet, TimeSpan limit)
    {
        DateTime started = DateTime.UtcNow;

        while (DateTime.UtcNow - started < limit && !done())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(true);
            DateTime last = lastEventUtc();
            DateTime reference = last < started ? started : last;

            if (DateTime.UtcNow - reference > quiet)
            {
                return;
            }
        }
    }

    private async Task<int> RunCoreAsync(string userId, string password)
    {
        object replyLib;
        ISKOrderLib order;

        try
        {
            _center = (ISKCenterLib)Create(SkComIds.CenterLibClsid);
            replyLib = Create(SkComIds.ReplyLibClsid);
            order = (ISKOrderLib)Create(SkComOrderIds.OrderLibClsid);
        }
        catch (Exception exception) when (exception is COMException or IOException or InvalidCastException or TypeLoadException)
        {
            _log.Event("create_failed", new Dictionary<string, object?> { ["hresult"] = $"0x{exception.HResult:X8}", ["message"] = exception.Message });
            ProbeLog.Say($"建立 SKCOM 物件失敗(0x{exception.HResult:X8})。請確認 SKCOM x64 已以系統管理員身分執行 install.bat 註冊。");
            return 3;
        }

        // A. SKCOM 版本
        string apiVersion = _redactor.Redact(Safe(() => Center.SKCenterLib_GetSKAPIVersionAndBit(userId)));
        _log.Event("api_version", new Dictionary<string, object?> { ["value"] = apiVersion });
        ProbeLog.Say($"SKCOM 版本:{apiVersion}");

        string skLogPath = Path.Combine(_options.OutputDirectory, "sklog");
        Directory.CreateDirectory(skLogPath);
        Report("set_log_path", Center.SKCenterLib_SetLogPath(skLogPath));

        ReplyEventsSink replySink = new();
        replySink.MessageReceived += message => _log.Event("announcement", new Dictionary<string, object?> { ["message"] = message });
        _connections.Add(ComEventConnection.Advise(replyLib, SkComIds.ReplyLibEventsIid, replySink));

        OrderEventsSink orderSink = new();
        HookOrderEvents(orderSink);
        _connections.Add(ComEventConnection.Advise(order, SkComOrderIds.OrderLibEventsIid, orderSink));

        if (!_options.AccountsOnly)
        {
            // 報價物件與 probe-tw / serve 一樣在登入前建立並掛事件;建不起來只讓那一段跳過
            CreateQuoteObjects();
        }

        ProbeLog.Say("登入群益(只登入這一次,失敗不重試)…");
        int loginCode = Center.SKCenterLib_Login(userId, password);
        Report("login", loginCode);

        if (loginCode != 0)
        {
            _log.Event("login_last_log", new Dictionary<string, object?> { ["value"] = Safe(Center.SKCenterLib_GetLastLogInfo) });
            ProbeLog.Say($"登入失敗(代碼 {loginCode}):{Safe(() => Center.SKCenterLib_GetReturnCodeMessage(loginCode))}。帳密類錯誤請勿連續重試,錯太多次會鎖帳號。");
            return 4;
        }

        // B. 期貨帳務查詢(唯讀)
        await RunAccountsAsync(order, userId).ConfigureAwait(true);

        if (_options.AccountsOnly)
        {
            ProbeLog.Say("--accounts-only:不連報價主機。");
        }
        else
        {
            // C / D / E 先全部訂好,再一起收集
            await StartDomesticAsync().ConfigureAwait(true);
            await StartOverseasAsync().ConfigureAwait(true);
            await StartOverseasOptionAsync().ConfigureAwait(true);

            ProbeLog.Say($"收集即時報價 {_options.Seconds} 秒…");
            await Task.Delay(TimeSpan.FromSeconds(_options.Seconds)).ConfigureAwait(true);
            LogQuoteSummary();
            LeaveQuoteMonitors();
        }

        _log.Event("done", new Dictionary<string, object?> { ["accounts"] = _redactor.AccountCount });
        ProbeExitGuard.Arm(0);
        ProbeLog.Say($"完成。輸出在 {_options.OutputDirectory}(sklog 子目錄是群益自己的紀錄,含帳號,不要給別人;其他檔案的登入帳號、帳號號碼、戶名都已遮罩)");
        return 0;
    }

    private void CreateQuoteObjects()
    {
        _domestic = TryCreate<ISKQuoteLib>(SkComDomesticIds.QuoteLibClsid, "tw");

        if (_domestic is not null)
        {
            QuoteEventsSink sink = new();
            HookDomesticEvents(sink);
            _connections.Add(ComEventConnection.Advise(_domestic, SkComDomesticIds.QuoteLibEventsIid, sink));
        }

        _overseas = TryCreate<ISKOSQuoteLib>(SkComIds.OsQuoteLibClsid, "os");

        if (_overseas is not null)
        {
            OsQuoteEventsSink sink = new();
            HookOverseasEvents(sink);
            _connections.Add(ComEventConnection.Advise(_overseas, SkComIds.OsQuoteLibEventsIid, sink));
        }

        _overseasOption = TryCreate<ISKOOQuoteLib>(SkComOverseasOptionIds.OoQuoteLibClsid, "oo");

        if (_overseasOption is not null)
        {
            OoQuoteEventsSink sink = new();
            HookOverseasOptionEvents(sink);
            _connections.Add(ComEventConnection.Advise(_overseasOption, SkComOverseasOptionIds.OoQuoteLibEventsIid, sink));
        }
    }

    private T? TryCreate<T>(Guid clsid, string prefix)
        where T : class
    {
        try
        {
            return (T)Create(clsid);
        }
        catch (Exception exception) when (exception is COMException or IOException or InvalidCastException or TypeLoadException)
        {
            _log.Event(prefix + "_create_failed", new Dictionary<string, object?> { ["hresult"] = $"0x{exception.HResult:X8}", ["message"] = exception.Message });
            ProbeLog.Say($"建立 {typeof(T).Name} 失敗(0x{exception.HResult:X8}),這一段跳過。");
            return null;
        }
    }

    private void Report(string step, int code, params (string Key, object? Value)[] extra)
    {
        Dictionary<string, object?> fields = new(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["message"] = Safe(() => Center.SKCenterLib_GetReturnCodeMessage(code)),
        };

        foreach ((string key, object? value) in extra)
        {
            fields[key] = value;
        }

        _log.Event(step, fields);
    }

    /// <summary>原文寫進 <paramref name="name"/>.txt;第一次寫這個檔時先寫一行欄位名稱。</summary>
    private void RawWithHeader(string name, string header, string line)
    {
        if (_rawHeaders.Add(name))
        {
            _log.Raw(name, header);
        }

        _log.Raw(name, line);
    }
}
