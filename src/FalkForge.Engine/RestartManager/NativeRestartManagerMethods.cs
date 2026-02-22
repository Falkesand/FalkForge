using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace FalkForge.Engine.RestartManager;

/// <summary>
/// P/Invoke declarations for the Windows Restart Manager API (rstrtmgr.dll).
/// Uses DllImport for methods requiring complex struct marshalling (ByValTStr).
/// NativeAOT compatible -- the runtime generates marshalling stubs at compile time.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeRestartManagerMethods
{
    internal const int ERROR_SUCCESS = 0;
    internal const int ERROR_MORE_DATA = 234;

    /// <summary>Maximum length of a Restart Manager session key string (including null terminator).</summary>
    internal const int CCH_RM_SESSION_KEY = 256;

    /// <summary>Maximum number of processes the Restart Manager can return in a single call.</summary>
    internal const int RM_MAX_PROCESSES = 128;

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmStartSession(
        out uint pSessionHandle,
        uint dwSessionFlags,
        [Out] char[] strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmRegisterResources(
        uint dwSessionHandle,
        uint nFiles,
        string[]? rgsFileNames,
        uint nApplications,
        RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        out uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmShutdown(
        uint dwSessionHandle,
        uint lActionFlags,
        nint fnStatus);

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmRestart(
        uint dwSessionHandle,
        uint dwRestartFlags,
        nint fnStatus);

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmEndSession(uint dwSessionHandle);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_SESSION_KEY + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;

        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    internal enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    internal enum RM_REBOOT_REASON : uint
    {
        RmRebootReasonNone = 0x0,
        RmRebootReasonPermissionDenied = 0x1,
        RmRebootReasonSessionMismatch = 0x2,
        RmRebootReasonCriticalProcess = 0x4,
        RmRebootReasonCriticalService = 0x8,
        RmRebootReasonDetectedSelf = 0x10
    }

    /// <summary>
    /// Graceful shutdown flag: RmShutdown without force.
    /// </summary>
    internal const uint RM_SHUTDOWN_TYPE_NORMAL = 0;

    /// <summary>
    /// Force shutdown flag. NEVER used in this implementation -- graceful only.
    /// </summary>
    internal const uint RM_FORCE_SHUTDOWN = 0x1;
}
