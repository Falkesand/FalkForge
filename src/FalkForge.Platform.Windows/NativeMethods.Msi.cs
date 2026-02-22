using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace FalkForge.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    [LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiInstallProductW(string szPackagePath, string? szCommandLine);

    [LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiConfigureProductW(string szProductCode, int iInstallLevel, int iInstallState);

    [LibraryImport("msi.dll")]
    internal static partial int MsiSetInternalUI(int dwUILevel, nint phWnd);

    internal const int INSTALLLEVEL_DEFAULT = 0;
    internal const int INSTALLSTATE_ABSENT = 2;
    internal const int INSTALLUILEVEL_NONE = 2;
    internal const uint ERROR_SUCCESS = 0;
    internal const uint ERROR_SUCCESS_REBOOT_REQUIRED = 3010;
}
