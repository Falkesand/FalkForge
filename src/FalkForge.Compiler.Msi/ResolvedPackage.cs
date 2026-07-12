using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi;

public sealed class ResolvedPackage
{
    public required PackageModel Package { get; init; }
    public required IReadOnlyList<ResolvedComponent> Components { get; init; }
    public required IReadOnlyList<ResolvedFile> Files { get; init; }

    /// <summary>
    /// Maps a feature-gated <see cref="ServiceModel.Name"/> to the id of the synthesized
    /// <see cref="ResolvedComponent"/> that carries its <see cref="ServiceModel.FeatureRef"/>.
    /// Only services with a non-null FeatureRef get an entry — services without one keep the
    /// legacy behavior of attaching to the component that owns their executable file (or the
    /// package's default component) via <see cref="ServiceInstallTableProducer"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ServiceFeatureComponents { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Maps the list index of a feature-gated entry in <see cref="PackageModel.RegistryEntries"/>
    /// to the id of the synthesized <see cref="ResolvedComponent"/> that carries its
    /// <see cref="RegistryEntryModel.FeatureRef"/>. Only entries with a non-null FeatureRef and no
    /// explicit <see cref="RegistryEntryModel.ComponentId"/> get an entry — an explicit
    /// ComponentId always wins (see <see cref="RegistryTableProducer"/>).
    /// </summary>
    public IReadOnlyDictionary<int, string> RegistryFeatureComponents { get; init; } =
        new Dictionary<int, string>();

    /// <summary>
    /// Maps the list index of a feature-gated entry in <see cref="PackageModel.Shortcuts"/> to
    /// the id of the synthesized <see cref="ResolvedComponent"/> that carries its
    /// <see cref="ShortcutModel.FeatureRef"/>. Only entries with a non-null FeatureRef get an
    /// entry — see <see cref="ShortcutTableProducer"/>.
    /// </summary>
    public IReadOnlyDictionary<int, string> ShortcutFeatureComponents { get; init; } =
        new Dictionary<int, string>();

    /// <summary>
    /// Maps the list index of a feature-gated entry in <see cref="PackageModel.EnvironmentVariables"/>
    /// to the id of the synthesized <see cref="ResolvedComponent"/> that carries its
    /// <see cref="EnvironmentVariableModel.FeatureRef"/>. Only entries with a non-null FeatureRef
    /// get an entry — see <see cref="EnvironmentTableProducer"/>.
    /// </summary>
    public IReadOnlyDictionary<int, string> EnvironmentFeatureComponents { get; init; } =
        new Dictionary<int, string>();

    /// <summary>
    /// Maps the list index of a feature-gated entry in <see cref="PackageModel.IniFiles"/> to the
    /// id of the synthesized <see cref="ResolvedComponent"/> that carries its
    /// <see cref="IniFileModel.FeatureRef"/>. Only entries with a non-null FeatureRef get an
    /// entry — see <see cref="IniFileTableProducer"/>.
    /// </summary>
    public IReadOnlyDictionary<int, string> IniFileFeatureComponents { get; init; } =
        new Dictionary<int, string>();

    /// <summary>
    /// Maps the list index of a feature-gated entry in <see cref="PackageModel.FileAssociations"/>
    /// to the id of the synthesized <see cref="ResolvedComponent"/> that carries its
    /// <see cref="FileAssociationModel.FeatureRef"/>. Only entries with a non-null FeatureRef get
    /// an entry — see <see cref="ExtensionTableProducer"/>.
    /// </summary>
    public IReadOnlyDictionary<int, string> FileAssociationFeatureComponents { get; init; } =
        new Dictionary<int, string>();

    /// <summary>
    ///     Per-instance identifier assigned at construction time.
    ///     Used as a build-nonce for PackageCode derivation in normal (non-reproducible)
    ///     mode: two separate <see cref="ResolvedPackage"/> instances that happen to share
    ///     identical content will still produce different PackageCodes, satisfying the MSI
    ///     requirement that distinct packaging events yield distinct PackageCodes.
    ///     Callers that need a stable PackageCode across multiple <c>MsiRecipeBuilder.Build</c>
    ///     calls should reuse the same <see cref="ResolvedPackage"/> instance.
    /// </summary>
    public Guid InstanceId { get; } = Guid.NewGuid();
}