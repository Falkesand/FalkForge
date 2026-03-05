using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Interop;

[SupportedOSPlatform("windows")]
internal sealed class FciHandle : SafeHandle
{
    public FciHandle() : base(nint.Zero, true)
    {
    }

    public FciHandle(nint handle) : base(nint.Zero, true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        return NativeMethods.FCIDestroy(handle);
    }
}