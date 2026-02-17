using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FalkForge.Engine.Variables;

internal sealed class SecureVariable : IDisposable
{
    private readonly byte[] _data;
    private GCHandle _pin;
    private bool _disposed;

    public SecureVariable(string value)
    {
        _data = Encoding.UTF8.GetBytes(value);
        _pin = GCHandle.Alloc(_data, GCHandleType.Pinned);
    }

    public string GetValue()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Encoding.UTF8.GetString(_data);
    }

    ~SecureVariable()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_data);
            if (_pin.IsAllocated) _pin.Free();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_data);
        if (_pin.IsAllocated) _pin.Free();
        GC.SuppressFinalize(this);
    }
}
