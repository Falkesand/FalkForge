namespace FalkForge.Engine.Detection;

using FalkForge.Engine.Protocol;

/// <summary>
/// Per-package detection outcome: the package's identifier, its detected installation
/// state, and its installed version when detectable (MSI packages with a ProductCode),
/// otherwise null. Produced by <see cref="PackageDetector.DetectPackageStates"/> and
/// surfaced to the UI as per-package lifecycle notifications.
/// </summary>
public readonly record struct PackageDetectionInfo(string PackageId, InstallState State, string? Version);
