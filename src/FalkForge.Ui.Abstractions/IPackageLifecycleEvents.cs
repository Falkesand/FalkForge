namespace FalkForge.Ui.Abstractions;

/// <summary>
/// Optional companion to <see cref="IInstallerEngine"/> for engines that surface granular,
/// per-package and per-related-bundle lifecycle notifications (matching WiX Burn bootstrapper
/// granularity). An engine that implements this interface raises these events, in chain order,
/// interleaved with the phase-level Detect/Plan/Apply round-trips. The shell forwards each to
/// the active <c>InstallerPage</c>'s corresponding hook.
/// <para>
/// All events are <b>observational</b>: they notify the UI but cannot veto or skip a package.
/// </para>
/// </summary>
public interface IPackageLifecycleEvents
{
    /// <summary>Raised once per package after its detection completes (Detect phase).</summary>
    event Action<PackageDetectInfo>? PackageDetected;

    /// <summary>Raised once per related bundle detected on the machine (Detect phase).</summary>
    event Action<RelatedBundleInfo>? RelatedBundleDetected;

    /// <summary>Raised once per package as its planning begins (Plan phase).</summary>
    event Action<PackagePlanInfo>? PackagePlanBeginning;

    /// <summary>Raised once per package after its planning completes (Plan phase).</summary>
    event Action<PackagePlanInfo>? PackagePlanCompleted;

    /// <summary>Raised once per package immediately before it is applied (Apply phase).</summary>
    event Action<PackageApplyBeginInfo>? PackageApplyBeginning;

    /// <summary>Raised once per package immediately after it is applied (Apply phase).</summary>
    event Action<PackageApplyCompleteInfo>? PackageApplyCompleted;
}
