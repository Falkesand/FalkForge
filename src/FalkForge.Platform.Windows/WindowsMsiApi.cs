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
#pragma warning disable IDE0052, S4487 // written-but-never-read is exactly the point: this field's
    // sole job is to be a GC root for the wrapper delegate. Production never needs its VALUE back
    // (msi.dll is the only consumer, via the native function pointer); only reflection-based tests
    // read it back to prove the rooting actually happened.
    private static NativeMethods.MsiInstallUIHandler? _rootedHandler;
#pragma warning restore IDE0052, S4487

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
            var previous = NativeMethods.MsiSetExternalUIW(nativeHandler, messageFilter, context);

            // Only re-root after msi.dll has confirmed the swap -- see the field comment above. The
            // OLD rooted delegate is already reachable via _rootedHandler itself up to this
            // reassignment (a static field is an unconditional GC root), so nothing extra is needed
            // to keep it alive through the native call above.
            _rootedHandler = nativeHandler;
            return previous;
        }
    }
}
