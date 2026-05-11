namespace FalkForge.Engine.Bootstrap;

/// <summary>
/// Abstraction over <see cref="ElevationProbe"/> that lets the orchestrator be tested without
/// real UAC token queries.
/// </summary>
public interface IElevationProbe
{
    /// <summary>
    /// Returns <see langword="true"/> when the current process token has UAC elevation
    /// (administrative privilege).
    /// </summary>
    bool IsElevated();
}
