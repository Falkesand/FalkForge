using System.Runtime.Versioning;

namespace FalkForge.Platform.Windows;

/// <summary>
/// Production implementation of <see cref="IMsiApi"/> that delegates to msi.dll via P/Invoke.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMsiApi : IMsiApi
{
    // MsiSetExternalUIW registers a process-global callback that Windows Installer can invoke for
    // as long as the process runs -- there is no "unregister" notification back to us and no bound
    // call scope. [LibraryImport] only roots the marshalled delegate for the duration of the P/Invoke
    // call itself; once SetExternalUI returns, nothing else keeps the wrapper lambda (or its native
    // thunk) alive, so the GC is free to collect it and msi.dll is left holding a dangling function
    // pointer (same defect shape as the FCI callback bug fixed in 8ca45416/f962d78f). Because the
    // external UI handler is a single process-global slot, a static field is the correct root: it
    // always holds whichever wrapper is CURRENTLY registered with msi.dll.
    //
    // Swap ordering matters: MsiSetExternalUIW is a synchronous swap, so once it returns, msi.dll has
    // already dropped its reference to the PREVIOUS handler and will never call it again -- it is
    // therefore safe to drop our root on the previous wrapper only AFTER the native call returns
    // (never before, or a call already in flight against the old handler could race a collection).
    // Guarded by Gate so a concurrent SetExternalUI call can't leave the static root out of step with
    // whatever msi.dll actually holds (the swap-then-root pair must be atomic).
    private static readonly object Gate = new();
    private static NativeMethods.MsiInstallUIHandler? _rootedHandler;

    public uint InstallProduct(string packagePath, string? commandLine)
        => NativeMethods.MsiInstallProductW(packagePath, commandLine);

    public uint ConfigureProduct(string productCode, int installLevel, int installState)
        => NativeMethods.MsiConfigureProductW(productCode, installLevel, installState);

    public int SetInternalUI(int uiLevel, nint window)
        => NativeMethods.MsiSetInternalUI(uiLevel, window);

    public nint SetExternalUI(MsiExternalUIHandler? handler, uint messageFilter, nint context)
    {
        NativeMethods.MsiInstallUIHandler? nativeHandler = handler is not null
            ? (ctx, msgType, msg) => handler(ctx, msgType, msg)
            : null;

        // The registration is process-global regardless of which WindowsMsiApi instance calls this
        // (there is exactly one msi.dll external-UI slot per process), so the swap-and-root logic
        // lives in a static method rather than mutating the static field directly from here.
        return SwapAndRootExternalUiHandler(nativeHandler, messageFilter, context);
    }

    private static nint SwapAndRootExternalUiHandler(
        NativeMethods.MsiInstallUIHandler? nativeHandler, uint messageFilter, nint context)
    {
        lock (Gate)
        {
            var previouslyRooted = _rootedHandler;
            var previous = NativeMethods.MsiSetExternalUIW(nativeHandler, messageFilter, context);

            // Only re-root after msi.dll has confirmed the swap -- see the field comment above. Keep
            // the OLD rooted delegate explicitly reachable through this point (belt-and-braces: it was
            // already reachable via _rootedHandler itself up to the reassignment below, but this makes
            // the "must outlive the swap call" intent impossible for a future refactor to silently
            // break).
            GC.KeepAlive(previouslyRooted);
            _rootedHandler = nativeHandler;
            return previous;
        }
    }
}
