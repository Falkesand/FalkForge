using System.Collections.Immutable;

namespace FalkForge.Decompiler.Recipe.Schemas;

/// <summary>
/// Raw row returned by <see cref="UpgradeSchema.Schema"/>.
/// </summary>
public sealed record UpgradeRow(
    string  UpgradeCode,
    string? VersionMin,
    string? VersionMax,
    string? Language,
    int     Attributes,
    string? Remove,
    string  ActionProperty);

/// <summary>
/// Declarative read schema for the MSI <c>Upgrade</c> table.
/// Columns: UpgradeCode (PK1), VersionMin (nullable), VersionMax (nullable),
///          Language (nullable), Attributes, Remove (nullable), ActionProperty (PK2).
/// </summary>
public static class UpgradeSchema
{
    public static readonly ReadColumn UpgradeCode    = new("UpgradeCode",    ReadColumnType.String,  false, 0);
    public static readonly ReadColumn VersionMin     = new("VersionMin",     ReadColumnType.String,  true,  1);
    public static readonly ReadColumn VersionMax     = new("VersionMax",     ReadColumnType.String,  true,  2);
    public static readonly ReadColumn Language       = new("Language",       ReadColumnType.String,  true,  3);
    public static readonly ReadColumn Attributes     = new("Attributes",     ReadColumnType.Integer, false, 4);
    public static readonly ReadColumn Remove         = new("Remove",         ReadColumnType.String,  true,  5);
    public static readonly ReadColumn ActionProperty = new("ActionProperty", ReadColumnType.String,  false, 6);

    public static readonly TableReadSchema<UpgradeRow> Schema = new(
        TableName: "Upgrade",
        Columns: [UpgradeCode, VersionMin, VersionMax, Language, Attributes, Remove, ActionProperty],
        Map: row => Result<UpgradeRow>.Success(new UpgradeRow(
            row.String(UpgradeCode),
            row.StringOrNull(VersionMin),
            row.StringOrNull(VersionMax),
            row.StringOrNull(Language),
            row.Int32(Attributes),
            row.StringOrNull(Remove),
            row.String(ActionProperty))));
}
