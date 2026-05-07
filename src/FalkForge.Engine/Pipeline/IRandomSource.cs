namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Abstracts entropy generation so tests can use a deterministic seed
/// instead of <see cref="System.Security.Cryptography.RandomNumberGenerator"/> and
/// <see cref="Guid.NewGuid"/>.
/// </summary>
public interface IRandomSource
{
    /// <summary>Returns a new unique identifier.</summary>
    Guid NewGuid();

    /// <summary>Fills <paramref name="buffer"/> with cryptographically random bytes.</summary>
    void Fill(Span<byte> buffer);
}
