using System.Collections.Immutable;
using FalkForge.Decompiler.Recipe.Schemas;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Immutable snapshot of all table rows read from an MSI database via
/// <see cref="MsiDecompiler.DecompileToRecipe"/>. Holds the raw typed row
/// collections produced by each <see cref="TableReadSchema{TRow}"/> before
/// the reconstructor stage runs.
/// <para>
/// This is the decompile-side intermediate representation, symmetric to
/// <c>MsiDatabaseRecipe</c> on the compile side. Tests and round-trip
/// tooling can assert directly against these row collections without
/// instantiating a <see cref="PackageModel"/>.
/// </para>
/// </summary>
public sealed record MsiReadRecipe
{
    public required IReadOnlyList<PropertyRow>          Properties        { get; init; }
    public required IReadOnlyList<DirectoryRow>         Directories       { get; init; }
    public required IReadOnlyList<ComponentRow>         Components        { get; init; }
    public required IReadOnlyList<FileRow>              Files             { get; init; }
    public required IReadOnlyList<FeatureRow>           Features          { get; init; }
    public required IReadOnlyList<FeatureComponentsRow> FeatureComponents { get; init; }
    public required IReadOnlyList<RegistryRow>          RegistryEntries   { get; init; }
    public required IReadOnlyList<ServiceRow>           Services          { get; init; }
    public required IReadOnlyList<ShortcutRow>          Shortcuts         { get; init; }
    public required IReadOnlyList<UpgradeRow>           Upgrades          { get; init; }

    /// <summary>
    /// Additional rows from extension-contributed tables, keyed by table name.
    /// Empty when no extension <see cref="FalkForge.Extensibility.IMsiTableContributor"/>
    /// instances with a <c>ReadSchema</c> were registered at decompile time.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<object>> ExtensionRows { get; init; }
        = ImmutableDictionary<string, IReadOnlyList<object>>.Empty;
}
