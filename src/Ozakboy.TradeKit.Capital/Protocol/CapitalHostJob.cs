using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ozakboy.TradeKit.Capital.Protocol;

/// <summary>
/// 一個設了 <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> 的 Windows Job Object:宿主行程放進來之後,父行程不論正常結束、當掉或
/// 被強制結束,系統關閉最後一個 job handle 時都會把宿主一起收掉,不會留下還掛著群益登入的孤兒宿主。由
/// <see cref="CapitalHostSession"/> 持有到 <see cref="CapitalHostSession.DisposeAsync"/>。只在 Windows 建立。
/// A Windows Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE. Once the host process is in it, the system ends the
/// host when the last job handle closes, whether the parent exits normally, crashes or is killed, so no orphaned host
/// stays logged in to Capital. Held by the session until DisposeAsync; created on Windows only.
/// </summary>
/// <remarks>
/// handle 不可繼承(安全屬性為 null),所以宿主或其他子行程不會意外延長 job 的壽命。行程啟動到放進 job 之間有一小段空窗
/// (<see cref="Process.Start()"/> 不支援先暫停再啟動);宿主自己另有 stdin EOF 與父行程看門狗兩道保險。
/// The handle is not inheritable (null security attributes), so neither the host nor any other child extends the job's
/// life. There is a short window between the process starting and joining the job (Process.Start cannot start
/// suspended); the host itself also ends on stdin EOF and has a parent watchdog.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class CapitalHostJob : IDisposable
{
    private readonly SafeJobHandle _handle;

    private CapitalHostJob(SafeJobHandle handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// 建立 job;失敗時回 <see langword="null"/> 與原因(不讓工作階段因此失敗,宿主仍有自己的保險)。
    /// Creates the job; null with the reason on failure, which never fails the session.
    /// </summary>
    /// <param name="problem">失敗原因。Why it failed.</param>
    /// <returns>Job,或 <see langword="null"/>。The job, or null.</returns>
    public static CapitalHostJob? TryCreate(out string problem)
    {
        SafeJobHandle handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);

        if (handle.IsInvalid)
        {
            problem = Describe("CreateJobObject");
            handle.Dispose();
            return null;
        }

        NativeMethods.JobObjectExtendedLimitInformation info = default;
        info.BasicLimitInformation.LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose;

        if (!NativeMethods.SetInformationJobObject(
                handle,
                NativeMethods.JobObjectExtendedLimitInformationClass,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>()))
        {
            problem = Describe("SetInformationJobObject");
            handle.Dispose();
            return null;
        }

        problem = string.Empty;
        return new CapitalHostJob(handle);
    }

    /// <summary>
    /// 把行程放進 job。
    /// Puts a process into the job.
    /// </summary>
    /// <param name="process">已啟動的行程。A started process.</param>
    /// <param name="problem">失敗原因。Why it failed.</param>
    /// <returns>成功時為 <see langword="true"/>。True on success.</returns>
    public bool TryAssign(Process process, out string problem)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            if (NativeMethods.AssignProcessToJobObject(_handle, process.SafeHandle))
            {
                problem = string.Empty;
                return true;
            }
        }
        catch (InvalidOperationException exception)
        {
            // 行程已經結束,拿不到控制代碼。The process already ended.
            problem = exception.Message;
            return false;
        }

        problem = Describe("AssignProcessToJobObject");
        return false;
    }

    /// <summary>
    /// 行程是否在這個 job 裡(測試用)。
    /// Whether the process is in this job, for tests.
    /// </summary>
    /// <param name="process">行程。The process.</param>
    /// <returns>在 job 裡時為 <see langword="true"/>。True when in the job.</returns>
    public bool Contains(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return NativeMethods.IsProcessInJob(process.SafeHandle, _handle, out bool result) && result;
    }

    /// <summary>關閉 job handle:job 裡還活著的行程會被系統結束。Closes the job handle; live processes in it are ended.</summary>
    public void Dispose() => _handle.Dispose();

    private static string Describe(string call)
    {
        int error = Marshal.GetLastPInvokeError();
        return $"{call} 失敗(Win32 {error}):{new Win32Exception(error).Message}";
    }
}
