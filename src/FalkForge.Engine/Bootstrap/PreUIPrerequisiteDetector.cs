namespace FalkForge.Engine.Bootstrap;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

/// <summary>
/// Probes the live system to determine which pre-UI prerequisites declared in the bundle
/// manifest are not yet installed.
/// </summary>
/// <remarks>
/// Phase 2 scope: detection only. No installation, no TaskDialog, no elevation.
/// Operates entirely in the NativeAOT bootstrapper before the managed WPF UI is launched.
/// AOT-safe: no reflection, no dynamic code generation, manual DI.
/// </remarks>
public sealed class PreUIPrerequisiteDetector
{
    private readonly SearchConditionEvaluator _evaluator;

    /// <summary>
    /// Initialises the detector with the platform services it needs to probe the system.
    /// </summary>
    /// <param name="registry">Registry abstraction for registry-based conditions.</param>
    /// <param name="fileSystemProvider">File system abstraction for file/directory conditions.</param>
    public PreUIPrerequisiteDetector(IRegistry registry, IFileSystemProvider fileSystemProvider)
    {
        _evaluator = new SearchConditionEvaluator(fileSystemProvider, registry);
    }

    /// <summary>
    /// Evaluates each declared pre-UI prerequisite against the live system and returns the
    /// subset that is not currently installed.
    /// </summary>
    /// <param name="declared">
    /// The list of pre-UI prerequisites from the installer manifest.
    /// Typically <see cref="InstallerManifest.PreUIPackages"/>.
    /// </param>
    /// <returns>
    /// A list of <see cref="PreUIPackageInfo"/> records whose search conditions did not all
    /// evaluate to <see langword="true"/>. Empty when every prerequisite is satisfied.
    /// </returns>
    public List<PreUIPackageInfo> FindMissing(IReadOnlyList<PreUIPackageInfo> declared)
    {
        if (declared.Count == 0)
            return [];

        var missing = new List<PreUIPackageInfo>();

        foreach (var prereq in declared)
        {
            if (!IsInstalled(prereq))
                missing.Add(prereq);
        }

        return missing;
    }

    /// <summary>
    /// Returns <see langword="true"/> when all search conditions for <paramref name="prereq"/>
    /// evaluate to <see langword="true"/> on the current system.
    /// A prerequisite with zero conditions is treated as already installed (pass-through).
    /// The Phase 1 validator BDL026 prevents zero-condition prereqs in real bundles.
    /// </summary>
    private bool IsInstalled(PreUIPackageInfo prereq)
    {
        // No conditions: treat as installed (pass-through semantics).
        // BDL026 enforces at-least-one condition at compile time; this path only
        // applies to malformed or test manifests.
        if (prereq.SearchConditions.Count == 0)
            return true;

        // ALL conditions must evaluate to true for the prereq to be considered installed.
        foreach (var condition in prereq.SearchConditions)
        {
            var result = _evaluator.Evaluate(condition);

            // Evaluation failure (e.g. unsupported type) → treat as not installed.
            // Better to attempt a redundant install than to silently skip a required prereq.
            if (result.IsFailure || !result.Value)
                return false;
        }

        return true;
    }
}
