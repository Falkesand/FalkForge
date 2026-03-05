using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Interop;

[SupportedOSPlatform("windows")]
internal sealed class FdiHandle : SafeHandle
{
    public FdiHandle() : base(nint.Zero, true)
    {
    }

    public FdiHandle(nint handle) : base(nint.Zero, true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        return NativeMethods.FDIDestroy(handle);
    }
}