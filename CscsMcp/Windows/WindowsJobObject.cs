using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace CscsMcp.Windows;

/// <summary>
/// A Windows Job Object that a sandbox worker is placed in. It gives the operating system limits
/// the worker cannot set on itself and cannot escape:
///   - a hard job-wide memory cap (the runtime's GC limit is inside the worker; this is outside it);
///   - an active-process cap, so the worker can start no child process at all — a native process
///     escape is blocked by the OS even though the interpreter already blocks the functions for it;
///   - kill-on-close: disposing this handle terminates every process in the job, so a worker can
///     never outlive the request, whatever state it is in.
/// It wraps a process that has already started (AssignProcess), which keeps the worker's stdio and
/// startup exactly as the non-isolated path. Windows only; the caller guards by platform.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsJobObject : IDisposable
{
    const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
    const uint JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200;
    const uint JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x00000400;
    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    const int JobObjectExtendedLimitInformation = 9;
    const int JobObjectCpuRateControlInformation = 15;

    const uint JOB_OBJECT_CPU_RATE_CONTROL_ENABLE = 0x1;
    const uint JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP = 0x4;

    readonly SafeJobHandle _handle;

    public WindowsJobObject(long jobMemoryBytes, int maxProcesses, int cpuHardCapPercent)
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid)
        {
            throw new InvalidOperationException("CreateJobObject failed: " + Marshal.GetLastWin32Error());
        }

        var ext = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        ext.BasicLimitInformation.LimitFlags =
            JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
            JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION;
        if (jobMemoryBytes > 0)
        {
            ext.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_JOB_MEMORY;
            ext.JobMemoryLimit = (nuint)jobMemoryBytes;
        }
        if (maxProcesses > 0)
        {
            ext.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
            ext.BasicLimitInformation.ActiveProcessLimit = (uint)maxProcesses;
        }
        Set(JobObjectExtendedLimitInformation, ext);

        if (cpuHardCapPercent is > 0 and < 100)
        {
            var cpu = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
            {
                ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP,
                // CpuRate is in hundredths of a percent of total capacity across all processors.
                CpuRate = (uint)(cpuHardCapPercent * 100),
            };
            Set(JobObjectCpuRateControlInformation, cpu);
        }
    }

    /// <summary>Places an already-started process in the job. Do this before it can spawn anything.</summary>
    public void AssignProcess(IntPtr processHandle)
    {
        if (!AssignProcessToJobObject(_handle, processHandle))
        {
            throw new InvalidOperationException("AssignProcessToJobObject failed: " + Marshal.GetLastWin32Error());
        }
    }

    /// <summary>Terminates every process in the job now, without waiting for Dispose.</summary>
    public void TerminateAll(uint exitCode = 1)
    {
        if (!_handle.IsInvalid)
        {
            TerminateJobObject(_handle, exitCode);
        }
    }

    void Set<T>(int infoClass, T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, buffer, false);
            if (!SetInformationJobObject(_handle, infoClass, buffer, (uint)size))
            {
                throw new InvalidOperationException($"SetInformationJobObject({infoClass}) failed: " + Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // Closing the handle terminates the job because of KILL_ON_JOB_CLOSE.
    public void Dispose() => _handle.Dispose();

    sealed class SafeJobHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
    {
        public uint ControlFlags;
        public uint CpuRate;
    }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeJobHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool SetInformationJobObject(SafeJobHandle hJob, int infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool AssignProcessToJobObject(SafeJobHandle hJob, IntPtr hProcess);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool TerminateJobObject(SafeJobHandle hJob, uint uExitCode);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);
}
