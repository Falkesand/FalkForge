using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Interop;

[SupportedOSPlatform("windows")]
internal sealed class MsiRecordHandle : SafeHandle
{
    public MsiRecordHandle() : base(nint.Zero, true)
    {
    }

    public MsiRecordHandle(nint handle) : base(nint.Zero, true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        return NativeMethods.MsiCloseHandle(handle) == NativeMethods.ERROR_SUCCESS;
    }
}