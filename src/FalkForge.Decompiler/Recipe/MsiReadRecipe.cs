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

    /// <summary>
    /// Every table name present in the source MSI database (from <see cref="IMsiTableAccess.GetTableNames"/>),
    /// including MSI-internal catalog tables (e.g. <c>_Tables</c>). Used by the migration report to
    /// name tables the migrator read nothing from, so a dropped table can never be silently reported
    /// as "all mapped". Required: in <see cref="MsiDecompiler"/>, a failed table-name query returns
    /// a <c>Result</c> failure and short-circuits before an <see cref="MsiReadRecipe"/> is ever
    /// constructed, so there is no real code path that reaches this property empty by default;
    /// making it required forces every caller that hand-builds a recipe to state its intent
    /// explicitly instead of silently re-enabling the "all tables read" claim via an unset default.
    /// </summary>
    public required IReadOnlyList<string> AllTableNames { get; init; }
}
