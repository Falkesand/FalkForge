using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
public sealed class MsiRecord : IDisposable
{
    private bool _disposed;

    internal MsiRecordHandle Handle { get; }

    public MsiRecord(uint fieldCount = 32)
    {
        var handle = NativeMethods.MsiCreateRecord(fieldCount);
        Handle = new MsiRecordHandle(handle);
    }

    public MsiRecord SetString(uint field, string? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = NativeMethods.MsiRecordSetString(Handle.DangerousGetHandle(), field, value);
        if (result != NativeMethods.ERROR_SUCCESS)
            throw new InvalidOperationException($"MsiRecordSetString failed for field {field}. Error code: {result}");
        return this;
    }

    public MsiRecord SetInteger(uint field, int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = NativeMethods.MsiRecordSetInteger(Handle.DangerousGetHandle(), field, value);
        if (result != NativeMethods.ERROR_SUCCESS)
            throw new InvalidOperationException($"MsiRecordSetInteger failed for field {field}. Error code: {result}");
        return this;
    }

    public MsiRecord SetStream(uint field, string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = NativeMethods.MsiRecordSetStream(Handle.DangerousGetHandle(), field, filePath);
        if (result != NativeMethods.ERROR_SUCCESS)
            throw new InvalidOperationException($"MsiRecordSetStream failed for field {field} with path '{filePath}'. Error code: {result}");
        return this;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Handle.Dispose();
            _disposed = true;
        }
    }
}
