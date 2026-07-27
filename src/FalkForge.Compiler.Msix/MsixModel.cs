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
    // No Shortcuts: AppxManifest has no shortcut element. Start menu entries come from
    // Applications/Application + its VisualElements, which the generator already emits.
    public IReadOnlyList<FileEntryModel> Files { get; init; } = [];
    public IReadOnlyList<MsixRegistryEntry> RegistryEntries { get; init; } = [];

    // Capabilities
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<string> RestrictedCapabilities { get; init; } = [];

    // Dependencies
    public string MinWindowsVersion { get; init; } = "10.0.17763.0";
    public string? MaxVersionTested { get; init; }
    public IReadOnlyList<MsixPackageDependency> Dependencies { get; init; } = [];

    // Extensions live on MsixApplication (FileTypeAssociations / Protocols): file type
    // associations and protocol handlers are Application-level extensions in AppxManifest,
    // and each category dictates its own XML namespace and required child elements.

    // VFS
    public VfsMappingMode VfsMapping { get; init; } = VfsMappingMode.Auto;
    public IReadOnlyList<VfsOverride> VfsOverrides { get; init; } = [];

    // Cross-cutting
    // No Scope: an MSIX package is always staged and registered per-user. Making it available
    // to every user on a machine is a deployment-time act (Add-AppxProvisionedPackage), not
    // something the package itself can declare.
    public SigningOptions? Signing { get; init; }
    public SbomOptions? SbomOptions { get; init; }

    // Auto-update
    public MsixUpdateSettings? UpdateSettings { get; init; }
}
