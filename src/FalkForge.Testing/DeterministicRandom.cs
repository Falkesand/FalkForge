namespace FalkForge.Testing;

using FalkForge.Engine.Pipeline;

/// <summary>
/// Deterministic <see cref="IRandomSource"/> for tests. Returns predictable GUIDs
/// (sequentially incremented) and fills buffers with a fixed byte pattern.
/// Thread-safe via interlocked counter for <see cref="NewGuid"/>.
/// </summary>
public sealed class DeterministicRandom : IRandomSource
{
    private int _guidCounter;
    private readonly byte _fillByte;

    /// <summary>
    /// Creates a deterministic random source. All <see cref="Fill"/> calls write
    /// <paramref name="fillByte"/> to every byte of the buffer.
    /// </summary>
    public DeterministicRandom(byte fillByte = 0xAB)
    {
        _fillByte = fillByte;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns a GUID whose last 4 bytes encode a sequentially incrementing counter,
    /// so each call produces a distinct but deterministic value.
    /// </remarks>
    public Guid NewGuid()
    {
        var counter = Interlocked.Increment(ref _guidCounter);
        // Encode the counter in the last 4 bytes of the GUID bytes
        var bytes = new byte[16];
        var counterBytes = BitConverter.GetBytes(counter);
        Array.Copy(counterBytes, 0, bytes, 12, 4);
        return new Guid(bytes);
    }

    /// <inheritdoc/>
    public void Fill(Span<byte> buffer) => buffer.Fill(_fillByte);
}
