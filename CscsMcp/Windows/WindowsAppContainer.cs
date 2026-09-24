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
    const uint WAIT_OBJECT_0 = 0;
    const uint WAIT_TIMEOUT = 0x102;
    const int UOI_NAME = 2;
    const int SE_WINDOW_OBJECT = 7;
    const uint DACL_SECURITY_INFORMATION = 0x4;
    const uint READ_CONTROL = 0x00020000;
    const uint WRITE_DAC = 0x00040000;
    const uint GENERIC_ALL = 0x10000000;
    const int GRANT_ACCESS = 1;
    const int TRUSTEE_IS_SID = 0;
    const int TRUSTEE_IS_UNKNOWN = 0;

    IntPtr _sid;

    WindowsAppContainer(IntPtr sid) => _sid = sid;

    /// <summary>
    /// Creates the AppContainer profile if it does not exist, and returns it. The profile is only a
    /// convenience (a per-container folder and registry hive, which the worker does not use: its TEMP
    /// is the run's own directory). CreateProcess needs just the SID, so when the profile cannot be
    /// created -- which can happen under a service's virtual account -- the SID is derived from the
    /// name instead.
    /// </summary>
    public static WindowsAppContainer Create(string name, string displayName, string description)
    {
        int created = CreateAppContainerProfile(name, displayName, description, IntPtr.Zero, 0, out var sid);
        if (created != 0)
        {
            int derived = DeriveAppContainerSidFromAppContainerName(name, out sid);
            if (derived != 0)
            {
                throw new InvalidOperationException(
                    $"AppContainer '{name}': CreateAppContainerProfile HRESULT 0x{created:X8}, DeriveAppContainerSidFromAppContainerName HRESULT 0x{derived:X8}.");
            }
        }
        var container = new WindowsAppContainer(sid);
        try
        {
            GrantWindowStationAndDesktop(sid);
        }
        catch
        {
            container.Dispose();
            throw;
        }
        return container;
    }

    /// <summary>
    /// Every process connects to its parent's window station and desktop while its Windows DLLs
    /// initialize (user32 does, and the .NET runtime loads it). A service runs on its own hidden,
    /// non-interactive window station, whose DACL does not include AppContainers, so a contained
    /// child dies before its first instruction with STATUS_DLL_INIT_FAILED (0xC0000142). Grant this
    /// container's SID -- not all app packages -- access to both. The desktop belongs to this service
    /// alone and shows no windows, so broad rights on it expose nothing; the file system and network
    /// stay denied. The grant lasts as long as the window station, i.e. until the service restarts,
    /// and Create runs again on the next start.
    /// </summary>
    static void GrantWindowStationAndDesktop(IntPtr sid)
    {
        var station = OpenWindowStation(ObjectName(GetProcessWindowStation()), false, READ_CONTROL | WRITE_DAC);
        if (station == IntPtr.Zero)
        {
            throw new InvalidOperationException($"OpenWindowStation failed: Win32 error {Marshal.GetLastWin32Error()}.");
        }
        try
        {
            AddAllowAce(station, sid, "window station");
        }
        finally
        {
            CloseWindowStation(station);
        }

        var desktop = OpenDesktop(ObjectName(GetThreadDesktop(GetCurrentThreadId())), 0, false, READ_CONTROL | WRITE_DAC);
        if (desktop == IntPtr.Zero)
        {
            throw new InvalidOperationException($"OpenDesktop failed: Win32 error {Marshal.GetLastWin32Error()}.");
        }
        try
        {
            AddAllowAce(desktop, sid, "desktop");
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    static string ObjectName(IntPtr handle)
    {
        GetUserObjectInformation(handle, UOI_NAME, null, 0, out var needed);
        var buffer = new char[Math.Max(needed / 2, 1)];
        if (!GetUserObjectInformation(handle, UOI_NAME, buffer, needed, out _))
        {
            throw new InvalidOperationException($"GetUserObjectInformation failed: Win32 error {Marshal.GetLastWin32Error()}.");
        }
        return new string(buffer).TrimEnd('\0');
    }

    static void AddAllowAce(IntPtr handle, IntPtr sid, string what)
    {
        uint err = GetSecurityInfo(handle, SE_WINDOW_OBJECT, DACL_SECURITY_INFORMATION,
            IntPtr.Zero, IntPtr.Zero, out var oldDacl, IntPtr.Zero, out var descriptor);
        if (err != 0)
        {
            throw new InvalidOperationException($"GetSecurityInfo ({what}) failed: Win32 error {err}.");
        }
        var newDacl = IntPtr.Zero;
        try
        {
            var access = new EXPLICIT_ACCESS
            {
                grfAccessPermissions = GENERIC_ALL,
                grfAccessMode = GRANT_ACCESS,
                grfInheritance = 0,
                Trustee = new TRUSTEE { TrusteeForm = TRUSTEE_IS_SID, TrusteeType = TRUSTEE_IS_UNKNOWN, ptstrName = sid },
            };
            err = SetEntriesInAcl(1, ref access, oldDacl, out newDacl);
            if (err != 0)
            {
                throw new InvalidOperationException($"SetEntriesInAcl ({what}) failed: Win32 error {err}.");
            }
            err = SetSecurityInfo(handle, SE_WINDOW_OBJECT, DACL_SECURITY_INFORMATION, IntPtr.Zero, IntPtr.Zero, newDacl, IntPtr.Zero);
            if (err != 0)
            {
                throw new InvalidOperationException($"SetSecurityInfo ({what}) failed: Win32 error {err}.");
            }
        }
        finally
        {
            if (newDacl != IntPtr.Zero)
            {
                LocalFree(newDacl);
            }
            LocalFree(descriptor);
        }
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

            var envBlock = BuildEnvironmentBlock(environment);
            PROCESS_INFORMATION pi;
            bool ok;
            int error;
            try
            {
                ok = CreateProcess(exePath, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    flags, envBlock, workingDir, ref startup, out pi);
                error = Marshal.GetLastWin32Error();
            }
            finally
            {
                Marshal.FreeHGlobal(envBlock); // CreateProcess copies it
            }
            if (!ok)
            {
                throw new InvalidOperationException($"CreateProcess (AppContainer) failed: Win32 error {error}.");
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TRUSTEE
    {
        public IntPtr pMultipleTrustee;
        public int MultipleTrusteeOperation;
        public int TrusteeForm;
        public int TrusteeType;
        public IntPtr ptstrName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct EXPLICIT_ACCESS
    {
        public uint grfAccessPermissions;
        public int grfAccessMode;
        public uint grfInheritance;
        public TRUSTEE Trustee;
    }

    [DllImport("user32", SetLastError = true)]
    static extern IntPtr GetProcessWindowStation();

    [DllImport("user32", SetLastError = true)]
    static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("kernel32")]
    static extern uint GetCurrentThreadId();

    [DllImport("user32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [Out] char[]? pvInfo, int nLength, out int lpnLengthNeeded);

    [DllImport("user32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32", SetLastError = true)]
    static extern bool CloseWindowStation(IntPtr hWinSta);

    [DllImport("user32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32", SetLastError = true)]
    static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("advapi32")]
    static extern uint GetSecurityInfo(IntPtr handle, int objectType, uint securityInfo, IntPtr ppsidOwner, IntPtr ppsidGroup, out IntPtr ppDacl, IntPtr ppSacl, out IntPtr ppSecurityDescriptor);

    [DllImport("advapi32")]
    static extern uint SetSecurityInfo(IntPtr handle, int objectType, uint securityInfo, IntPtr psidOwner, IntPtr psidGroup, IntPtr pDacl, IntPtr pSacl);

    [DllImport("advapi32", CharSet = CharSet.Unicode)]
    static extern uint SetEntriesInAcl(uint cCountOfExplicitEntries, ref EXPLICIT_ACCESS pListOfExplicitEntries, IntPtr oldAcl, out IntPtr newAcl);

    [DllImport("kernel32")]
    static extern IntPtr LocalFree(IntPtr hMem);

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
