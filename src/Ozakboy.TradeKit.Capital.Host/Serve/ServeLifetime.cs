using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// 父行程看門狗:啟動時以 PID 開住父行程(握著行程控制代碼,PID 被重複使用也不會認錯),每隔一段時間檢查它還在不在;
/// 不在了就呼叫 <c>onGone</c>(視同 stdin 結束)。stdin 的 EOF 是主要訊號,這是保險:父行程的 stdin 寫入端若被其他行程繼承,
/// 父行程消失後 EOF 不會來。
/// A parent-process watchdog. The parent is opened by PID at start-up (holding its process handle, so a reused PID is
/// never mistaken for it) and checked at an interval; once it is gone <c>onGone</c> runs, as if stdin had ended. EOF on
/// stdin is the main signal and this is the safety net: if another process inherited the write end of stdin, EOF never
/// comes after the parent disappears.
/// </summary>
internal sealed class ParentWatch : IDisposable
{
    private readonly Func<bool> _isAlive;
    private readonly Action _onGone;
    private readonly IDisposable? _owned;
    private readonly ITimer _timer;
    private int _fired;

    /// <summary>
    /// 以可程式化的存活檢查建立(測試用)。
    /// Creates the watch with a scriptable liveness check, for tests.
    /// </summary>
    /// <param name="isAlive">父行程是否還在。Whether the parent is still there.</param>
    /// <param name="interval">檢查間隔。The check interval.</param>
    /// <param name="onGone">父行程不在時呼叫一次。Called once when the parent is gone.</param>
    /// <param name="time">時間來源。The time source.</param>
    /// <param name="owned">一起釋放的資源(父行程物件)。A resource disposed along with the watch.</param>
    public ParentWatch(Func<bool> isAlive, TimeSpan interval, Action onGone, TimeProvider time, IDisposable? owned = null)
    {
        _isAlive = isAlive ?? throw new ArgumentNullException(nameof(isAlive));
        _onGone = onGone ?? throw new ArgumentNullException(nameof(onGone));
        ArgumentNullException.ThrowIfNull(time);
        _owned = owned;
        _timer = time.CreateTimer(_ => Check(), null, interval, interval);
    }

    /// <summary>
    /// 依 PID 開始看門;沒有 PID 時回 <see langword="null"/>(只靠 stdin EOF);父行程已經不在時立刻呼叫 <paramref name="onGone"/>。
    /// Starts watching by PID; null without a PID (stdin EOF only); calls onGone at once when the parent is already gone.
    /// </summary>
    /// <param name="parentPid">父行程 PID(用戶端以 <c>CAPITAL_PARENT_PID</c> 傳入)。The parent PID from CAPITAL_PARENT_PID.</param>
    /// <param name="interval">檢查間隔(預設 5 秒)。The check interval, 5 s by default.</param>
    /// <param name="onGone">父行程不在時呼叫一次。Called once when the parent is gone.</param>
    /// <param name="log">紀錄。The log.</param>
    /// <param name="time">時間來源。The time source.</param>
    /// <returns>看門狗,或 <see langword="null"/>。The watch, or null.</returns>
    public static ParentWatch? Start(int? parentPid, TimeSpan interval, Action onGone, HostLog log, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(onGone);
        ArgumentNullException.ThrowIfNull(log);

        if (parentPid is not { } pid)
        {
            log.Write("沒有 CAPITAL_PARENT_PID,只以 stdin 結束判斷父行程是否離開。");
            return null;
        }

        Process parent;

        try
        {
            parent = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            log.Write(string.Create(CultureInfo.InvariantCulture, $"父行程 {pid} 已經不在,結束。"));
            onGone();
            return null;
        }

        log.Write(string.Create(CultureInfo.InvariantCulture, $"看守父行程 {pid}(每 {interval.TotalSeconds:F0} 秒)。"));
        return new ParentWatch(() => IsAlive(parent), interval, onGone, time, parent);
    }

    /// <summary>
    /// 讀 <c>CAPITAL_PARENT_PID</c>(正整數才算)。
    /// Reads CAPITAL_PARENT_PID; only a positive integer counts.
    /// </summary>
    /// <returns>PID,或 <see langword="null"/>。The PID, or null.</returns>
    public static int? ReadParentPid() =>
        int.TryParse(Environment.GetEnvironmentVariable(ServeEnvironmentNames.ParentPid), NumberStyles.None, CultureInfo.InvariantCulture, out int pid) && pid > 0
            ? pid
            : null;

    /// <inheritdoc/>
    public void Dispose()
    {
        _timer.Dispose();
        _owned?.Dispose();
    }

    private static bool IsAlive(Process parent)
    {
        try
        {
            return !parent.HasExited;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // 讀不到狀態就當作還在:寧可多等 EOF,也不要誤殺。An unreadable state counts as alive; better to wait than to misfire.
            return true;
        }
    }

    private void Check()
    {
        if (Volatile.Read(ref _fired) != 0 || _isAlive())
        {
            return;
        }

        if (Interlocked.Exchange(ref _fired, 1) == 0)
        {
            _timer.Dispose();
            _onGone();
        }
    }
}

/// <summary>
/// 結束保險:決定要結束之後(stdin 結束、父行程不在、或協定層已回傳),給有序收尾一段寬限;時間到行程還在就強制結束。
/// 防的是 STA 被卡住(例如 SKCOM 的同步呼叫不回來)時協定層根本處理不到結束訊號,宿主變成孤兒、一直掛著群益登入。
/// An exit safety net: once the host decides to end (stdin ended, the parent is gone, or the protocol layer returned) the
/// orderly shutdown gets a grace period, after which a process still alive is ended forcibly. It covers an STA stuck in
/// a synchronous SKCOM call, where the protocol layer never gets to handle the end signal and the host would linger as
/// an orphan still logged in to Capital.
/// </summary>
internal sealed class ExitGuard : IDisposable
{
    private readonly TimeSpan _grace;
    private readonly Action<int> _forceExit;
    private readonly HostLog _log;
    private readonly TimeProvider _time;
    private ITimer? _timer;
    private int _armed;

    /// <summary>
    /// 建立保險。
    /// Creates the safety net.
    /// </summary>
    /// <param name="grace">寬限。The grace period.</param>
    /// <param name="forceExit">強制結束(真宿主是 <see cref="Environment.Exit(int)"/>)。The forced exit.</param>
    /// <param name="log">紀錄。The log.</param>
    /// <param name="time">時間來源。The time source.</param>
    public ExitGuard(TimeSpan grace, Action<int> forceExit, HostLog log, TimeProvider time)
    {
        _grace = grace;
        _forceExit = forceExit ?? throw new ArgumentNullException(nameof(forceExit));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// 開始倒數(只有第一次有效)。
    /// Starts the countdown; only the first call counts.
    /// </summary>
    /// <param name="reason">原因(寫進紀錄)。The reason, for the log.</param>
    /// <param name="exitCode">強制結束時的結束碼。The exit code of a forced exit.</param>
    public void Arm(string reason, int exitCode)
    {
        if (Interlocked.Exchange(ref _armed, 1) != 0)
        {
            return;
        }

        _timer = _time.CreateTimer(
            _ =>
            {
                _log.Write(string.Create(CultureInfo.InvariantCulture, $"{reason}之後 {_grace.TotalSeconds:F0} 秒內沒有正常結束,強制結束(結束碼 {exitCode})。"));
                _forceExit(exitCode);
            },
            null,
            _grace,
            Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc/>
    public void Dispose() => _timer?.Dispose();
}
