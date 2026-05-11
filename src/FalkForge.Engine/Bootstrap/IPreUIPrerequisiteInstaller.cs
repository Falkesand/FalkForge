namespace FalkForge.Engine.Bootstrap;

using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Abstraction over <see cref="PreUIPrerequisiteInstaller"/> that lets the orchestrator be
/// tested without real child-process spawning.
/// </summary>
public interface IPreUIPrerequisiteInstaller
{
    /// <summary>
    /// Runs all <paramref name="missing"/> packages sequentially.
    /// Returns a <see cref="PreUIResult"/> discriminated union describing the outcome.
    /// </summary>
    Task<PreUIResult> RunAllAsync(
        IReadOnlyList<PreUIPackageInfo> missing,
        IProgressSink progress,
        CancellationToken cancellationToken);
}
