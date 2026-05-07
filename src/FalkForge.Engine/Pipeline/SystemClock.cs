namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Production <see cref="ISystemClock"/> that delegates to
/// <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
public sealed class SystemClock : ISystemClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
