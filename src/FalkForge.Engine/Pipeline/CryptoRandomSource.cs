namespace FalkForge.Engine.Pipeline;

using System.Security.Cryptography;

/// <summary>
/// Production <see cref="IRandomSource"/> backed by
/// <see cref="RandomNumberGenerator"/> and <see cref="Guid.NewGuid"/>.
/// All operations are cryptographically strong and suitable for nonce generation.
/// </summary>
public sealed class CryptoRandomSource : IRandomSource
{
    /// <inheritdoc/>
    public Guid NewGuid() => Guid.NewGuid();

    /// <inheritdoc/>
    /// <remarks>Delegates to <see cref="RandomNumberGenerator.Fill"/> — no allocation.</remarks>
    public void Fill(Span<byte> buffer)
    {
        if (buffer.IsEmpty) return;
        RandomNumberGenerator.Fill(buffer);
    }
}
