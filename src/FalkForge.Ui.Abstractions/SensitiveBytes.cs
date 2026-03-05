namespace FalkForge.Ui.Abstractions;

using System.Security.Cryptography;

/// <summary>
/// A disposable wrapper around a byte array containing sensitive data.
/// Zeroes the underlying memory via <see cref="CryptographicOperations.ZeroMemory"/>
/// when disposed. Always consume via a <c>using</c> statement or declaration.
/// Do not copy — all copies share the same underlying array.
/// </summary>
public readonly struct SensitiveBytes : IDisposable
{
    private readonly byte[]? _data;

    /// <summary>
    /// Wraps <paramref name="data"/>. Ownership transfers to this instance —
    /// the caller must not retain or modify the array after construction.
    /// </summary>
    public SensitiveBytes(byte[] data) => _data = data;

    public ReadOnlySpan<byte> Span => _data;

    public int Length => _data?.Length ?? 0;

    public bool IsEmpty => _data is null or { Length: 0 };

    public void Dispose()
    {
        if (_data is not null)
            CryptographicOperations.ZeroMemory(_data);
    }
}
