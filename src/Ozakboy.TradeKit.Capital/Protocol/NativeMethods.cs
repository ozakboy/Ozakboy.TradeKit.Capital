using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Ozakboy.TradeKit.Capital.Protocol;

/// <summary>
/// Windows Job Object 的 P/Invoke(source-generated <see cref="LibraryImportAttribute"/>),給 <see cref="CapitalHostJob"/> 用。
/// The Windows Job Object P/Invokes, source-generated, used by <see cref="CapitalHostJob"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    /// <summary>JobObjectExtendedLimitInformation。</summary>
    public const int JobObjectExtendedLimitInformationClass = 9;

    /// <summary>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE:最後一個 job handle 關閉時結束 job 裡的所有行程。Kill every process when the last handle closes.</summary>
    public const uint JobObjectLimitKillOnJobClose = 0x2000;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial SafeJobHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(SafeJobHandle job, int infoClass, ref JobObjectExtendedLimitInformation info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsProcessInJob(SafeProcessHandle process, SafeJobHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    /// <summary>JOBOBJECT_BASIC_LIMIT_INFORMATION(x64 版面,欄位順序照 winnt.h)。The basic limits, in winnt.h order.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    /// <summary>IO_COUNTERS。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    /// <summary>JOBOBJECT_EXTENDED_LIMIT_INFORMATION。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}

/// <summary>
/// Job Object 的控制代碼;釋放時 CloseHandle(設了 KILL_ON_JOB_CLOSE 的 job 會因此收掉裡面的行程)。
/// A Job Object handle; releasing it closes the handle, which ends the processes of a KILL_ON_JOB_CLOSE job.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>給 P/Invoke 封送用。For P/Invoke marshalling.</summary>
    public SafeJobHandle()
        : base(ownsHandle: true)
    {
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
