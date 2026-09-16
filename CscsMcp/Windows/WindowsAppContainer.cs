using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CscsMcp.Windows;

/// <summary>
/// Launches the sandbox worker inside a Windows AppContainer: the same isolation Store apps and the
/// Edge renderer run in. With no capabilities granted, the process gets no network of any kind and
/// no access to any securable object that does not explicitly grant the container — so it cannot
/// read the service's own files, the other services' folders, or anywhere on disk except the two
/// places granted at deploy time (its own binaries, read+execute, and the runs directory it works
/// in). That is the layer that would still hold if a bug let compiled code run: file and network
/// are denied by the OS, not by the interpreter.
///
/// The worker talks over files (request.json / response.json in the working directory), not pipes,
/// because granting a container access to one directory is a one-time icacls at deploy time, where
/// handing inheritable pipe handles into a container is fragile. See README for the icacls steps.
///
/// Windows only. The profile SID is held for the object's lifetime and freed on Dispose.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppContainer : IDisposable
{
    const int PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES = 0x00020009;
    const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    const uint CREATE_SUSPENDED = 0x00000004;
    const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    const uint CREATE_NO_WINDOW = 0x08000000;
    const int ERROR_ALREADY_EXISTS = unchecked((int)0x800700B7);
    const uint WAIT_OBJECT_0 = 0;
    const uint WAIT_TIMEOUT = 0x102;

    IntPtr _sid;

    WindowsAppContainer(IntPtr sid) => _sid = sid;

    /// <summary>Creates the AppContainer profile if it does not exist, and returns it.</summary>
    public static WindowsAppContainer Create(string name, string displayName, string description)
    {
        int hr = CreateAppContainerProfile(name, displayName, description, IntPtr.Zero, 0, out var sid);
        if (hr == ERROR_ALREADY_EXISTS)
        {
            hr = DeriveAppContainerSidFromAppContainerName(name, out sid);
        }
        if (hr != 0)
        {
            throw new InvalidOperationException($"AppContainer profile '{name}' could not be created (HRESULT 0x{hr:X8}).");
        }
        return new WindowsAppContainer(sid);
    }

    /// <summary>
    /// Starts the worker suspended inside this container, places it in the job, then resumes it.
    /// Suspended-then-assign-then-resume is what guarantees the process is already inside the job's
    /// limits before it runs its first instruction.
    /// </summary>
    public LaunchedProcess Launch(string exePath, string arguments, string workingDir, IReadOnlyDictionary<string, string> environment, WindowsJobObject job)
    {
        var caps = new SECURITY_CAPABILITIES
        {
            AppContainerSid = _sid,
            Capabilities = IntPtr.Zero,
            CapabilityCount = 0,
            Reserved = 0,
        };

        var attrListSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
        var attrList = Marshal.AllocHGlobal(attrListSize);
        var capsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_CAPABILITIES>());
        try
        {
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref attrListSize))
            {
                throw new InvalidOperationException("InitializeProcThreadAttributeList failed: " + Marshal.GetLastWin32Error());
            }
            Marshal.StructureToPtr(caps, capsPtr, false);
            if (!UpdateProcThreadAttribute(attrList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES,
                    capsPtr, (IntPtr)Marshal.SizeOf<SECURITY_CAPABILITIES>(), IntPtr.Zero, IntPtr.Zero))
            {
                throw new InvalidOperationException("UpdateProcThreadAttribute failed: " + Marshal.GetLastWin32Error());
            }

            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startup.lpAttributeList = attrList;

            var commandLine = new StringBuilder("\"").Append(exePath).Append("\" ").Append(arguments);
            var flags = EXTENDED_STARTUPINFO_PRESENT | CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW;

            var ok = CreateProcess(exePath, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                flags, BuildEnvironmentBlock(environment), workingDir, ref startup, out var pi);
            if (!ok)
            {
                throw new InvalidOperationException("CreateProcess (AppContainer) failed: " + Marshal.GetLastWin32Error());
            }

            try
            {
                job.AssignProcess(pi.hProcess);
                if (ResumeThread(pi.hThread) == unchecked((uint)-1))
                {
                    throw new InvalidOperationException("ResumeThread failed: " + Marshal.GetLastWin32Error());
                }
                return new LaunchedProcess(pi.hProcess, pi.hThread);
            }
            catch
            {
                TerminateProcess(pi.hProcess, 1);
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
                throw;
            }
        }
        finally
        {
            if (attrList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attrList);
                Marshal.FreeHGlobal(attrList);
            }
            Marshal.FreeHGlobal(capsPtr);
        }
    }

    static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in environment.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(key).Append('=').Append(value).Append('\0');
        }
        sb.Append('\0'); // double-null terminated
        return Marshal.StringToHGlobalUni(sb.ToString());
    }

    public void Dispose()
    {
        if (_sid != IntPtr.Zero)
        {
            FreeSid(_sid);
            _sid = IntPtr.Zero;
        }
    }

    /// <summary>A running process: wait for it, read its exit code, or terminate it.</summary>
    [SupportedOSPlatform("windows")]
    public sealed class LaunchedProcess(IntPtr process, IntPtr thread) : IDisposable
    {
        public IntPtr Handle => process;

        /// <summary>True if it exited within the timeout; false if it is still running.</summary>
        public bool WaitForExit(int milliseconds) =>
            WaitForSingleObject(process, (uint)milliseconds) == WAIT_OBJECT_0;

        public bool HasExited => WaitForSingleObject(process, 0) == WAIT_OBJECT_0;

        public int ExitCode => GetExitCodeProcess(process, out var code) ? (int)code : -1;

        public void Terminate()
        {
            if (WaitForSingleObject(process, 0) == WAIT_TIMEOUT)
            {
                TerminateProcess(process, 1);
            }
        }

        public void Dispose()
        {
            CloseHandle(thread);
            CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SECURITY_CAPABILITIES
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("userenv", CharSet = CharSet.Unicode)]
    static extern int CreateAppContainerProfile(string pszAppContainerName, string pszDisplayName, string pszDescription, IntPtr pCapabilities, uint dwCapabilityCount, out IntPtr ppSidAppContainerSid);

    [DllImport("userenv", CharSet = CharSet.Unicode)]
    static extern int DeriveAppContainerSidFromAppContainerName(string pszAppContainerName, out IntPtr ppsidAppContainerSid);

    [DllImport("advapi32")]
    static extern IntPtr FreeSid(IntPtr pSid);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32", SetLastError = true)]
    static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string? lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32", SetLastError = true)]
    static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);
}
