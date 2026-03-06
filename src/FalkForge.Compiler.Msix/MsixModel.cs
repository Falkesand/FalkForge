using FalkForge.Models;
using FalkForge.Sbom;

namespace FalkForge.Compiler.Msix;

public sealed class MsixModel
{
    // Identity
    public required string Name { get; init; }
    public required string Publisher { get; init; }
    public required Version Version { get; init; }
    public ProcessorArchitecture Architecture { get; init; } = ProcessorArchitecture.X64;

    // Properties
    public required string DisplayName { get; init; }
    public required string PublisherDisplayName { get; init; }
    public string? Description { get; init; }
    public string? LogoPath { get; init; }

    // Applications
    public required IReadOnlyList<MsixApplication> Applications { get; init; }

    // Content
    public IReadOnlyList<FileEntryModel> Files { get; init; } = [];
    public IReadOnlyList<MsixRegistryEntry> RegistryEntries { get; init; } = [];
    public IReadOnlyList<ShortcutModel> Shortcuts { get; init; } = [];

    // Capabilities
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<string> RestrictedCapabilities { get; init; } = [];

    // Dependencies
    public string MinWindowsVersion { get; init; } = "10.0.17763.0";
    public string? MaxVersionTested { get; init; }
    public IReadOnlyList<MsixPackageDependency> Dependencies { get; init; } = [];

    // Extensions
    public IReadOnlyList<MsixExtension> Extensions { get; init; } = [];

    // VFS
    public VfsMappingMode VfsMapping { get; init; } = VfsMappingMode.Auto;
    public IReadOnlyList<VfsOverride> VfsOverrides { get; init; } = [];

    // Cross-cutting
    public InstallScope Scope { get; init; } = InstallScope.PerMachine;
    public SigningOptions? Signing { get; init; }
    public SbomOptions? SbomOptions { get; init; }

    // Auto-update
    public MsixUpdateSettings? UpdateSettings { get; init; }
}
