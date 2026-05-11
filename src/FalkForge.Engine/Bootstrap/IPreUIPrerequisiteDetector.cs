namespace FalkForge.Engine.Bootstrap;

using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Abstraction over <see cref="PreUIPrerequisiteDetector"/> that lets the orchestrator be
/// tested without real registry or file-system access.
/// </summary>
public interface IPreUIPrerequisiteDetector
{
    /// <summary>
    /// Returns the subset of <paramref name="declared"/> packages that are not currently
    /// installed on this system.
    /// </summary>
    List<PreUIPackageInfo> FindMissing(IReadOnlyList<PreUIPackageInfo> declared);
}
