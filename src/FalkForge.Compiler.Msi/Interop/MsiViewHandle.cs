using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Interop;

[SupportedOSPlatform("windows")]
internal sealed class MsiViewHandle : SafeHandle
{
    public MsiViewHandle() : base(nint.Zero, ownsHandle: true) { }
    public MsiViewHandle(nint handle) : base(nint.Zero, ownsHandle: true) => SetHandle(handle);

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        return NativeMethods.MsiCloseHandle(handle) == NativeMethods.ERROR_SUCCESS;
    }
}
