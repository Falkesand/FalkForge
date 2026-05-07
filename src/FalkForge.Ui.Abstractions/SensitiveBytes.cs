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

    /// <summary>
    /// Copies <paramref name="plaintext"/> into a new private backing array and returns
    /// a <see cref="SensitiveBytes"/> that owns it. The original span is unmodified;
    /// the caller remains responsible for zeroing their source buffer.
    /// </summary>
    public static SensitiveBytes FromPlaintext(ReadOnlySpan<byte> plaintext)
    {
        var copy = plaintext.ToArray();
        return new SensitiveBytes(copy);
    }

    /// <summary>
    /// Opens a short-lived reveal scope that exposes the plaintext bytes as a
    /// <see cref="ReadOnlySpan{T}"/> for the duration of the scope.
    /// Disposing the returned <see cref="RevealScope"/> does not zero the backing
    /// array — only disposing the outer <see cref="SensitiveBytes"/> does that.
    /// Use this when a caller needs transient plaintext access (e.g., writing to a pipe).
    /// </summary>
    public RevealScope Borrow() => new(_data);

    /// <summary>
    /// A short-lived scope exposing the plaintext bytes of a <see cref="SensitiveBytes"/>
    /// instance. Dispose when done with the plaintext; no zeroing occurs on dispose.
    /// </summary>
    public readonly struct RevealScope : IDisposable
    {
        private readonly byte[]? _data;

        internal RevealScope(byte[]? data) => _data = data;

        /// <summary>Plaintext bytes. Valid only while this scope is alive.</summary>
        public ReadOnlySpan<byte> Span => _data;

        /// <summary>Number of plaintext bytes.</summary>
        public int Length => _data?.Length ?? 0;

        /// <summary>Ends the reveal scope. Does not zero the backing array.</summary>
        public void Dispose() { /* intentional no-op — zeroing is SensitiveBytes.Dispose's job */ }
    }

    public void Dispose()
    {
        if (_data is not null)
            CryptographicOperations.ZeroMemory(_data);
    }
}
