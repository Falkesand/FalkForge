using System.Runtime.Versioning;

namespace FalkForge.Platform.Windows;

/// <summary>
/// Production implementation of <see cref="IMsiApi"/> that delegates to msi.dll via P/Invoke.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMsiApi : IMsiApi
{
    public uint InstallProduct(string packagePath, string? commandLine)
        => NativeMethods.MsiInstallProductW(packagePath, commandLine);

    public uint ConfigureProduct(string productCode, int installLevel, int installState)
        => NativeMethods.MsiConfigureProductW(productCode, installLevel, installState);

    public int SetInternalUI(int uiLevel, nint window)
        => NativeMethods.MsiSetInternalUI(uiLevel, window);

    public nint SetExternalUI(Delegate? handler, uint messageFilter, nint context)
        => NativeMethods.MsiSetExternalUIW(
            handler as NativeMethods.MsiInstallUIHandler,
            messageFilter,
            context);
}
