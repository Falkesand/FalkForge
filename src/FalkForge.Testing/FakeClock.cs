namespace FalkForge.Testing;

using FalkForge.Engine.Pipeline;

/// <summary>
/// Deterministic <see cref="ISystemClock"/> for tests. Starts at a fixed epoch
/// and advances only via <see cref="Advance"/>.
/// </summary>
public sealed class FakeClock : ISystemClock
{
    private DateTimeOffset _now;

    /// <summary>
    /// Creates a fake clock starting at <paramref name="startTime"/>.
    /// Defaults to 2024-01-01T00:00:00Z when not specified.
    /// </summary>
    public FakeClock(DateTimeOffset? startTime = null)
    {
        _now = startTime ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => _now;

    /// <summary>
    /// Advances the clock by <paramref name="delta"/>.
    /// </summary>
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    /// <summary>
    /// Sets the clock to a specific instant.
    /// </summary>
    public void Set(DateTimeOffset instant) => _now = instant;
}
