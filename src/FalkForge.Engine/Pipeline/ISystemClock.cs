namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Abstracts wall-clock time access so tests can use a deterministic fake clock
/// instead of <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
public interface ISystemClock
{
    /// <summary>Returns the current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}
