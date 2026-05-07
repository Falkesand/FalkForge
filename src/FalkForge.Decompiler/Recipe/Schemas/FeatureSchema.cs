using System.Collections.Immutable;

namespace FalkForge.Decompiler.Recipe.Schemas;

/// <summary>
/// Raw row returned by <see cref="FeatureSchema.Schema"/>.
/// </summary>
public sealed record FeatureRow(
    string  Feature,
    string? Feature_Parent,
    string  Title,
    string? Description,
    int     Display,
    int     Level,
    string? Directory_,
    int     Attributes);

/// <summary>
/// Declarative read schema for the MSI <c>Feature</c> table.
/// Columns: Feature (PK), Feature_Parent, Title, Description, Display, Level, Directory_, Attributes.
/// </summary>
public static class FeatureSchema
{
    public static readonly ReadColumn Feature        = new("Feature",        ReadColumnType.String,  false, 0);
    public static readonly ReadColumn Feature_Parent = new("Feature_Parent", ReadColumnType.String,  true,  1);
    public static readonly ReadColumn Title          = new("Title",          ReadColumnType.String,  false, 2);
    public static readonly ReadColumn Description    = new("Description",    ReadColumnType.String,  true,  3);
    public static readonly ReadColumn Display        = new("Display",        ReadColumnType.Integer, false, 4);
    public static readonly ReadColumn Level          = new("Level",          ReadColumnType.Integer, false, 5);
    public static readonly ReadColumn Directory_     = new("Directory_",     ReadColumnType.String,  true,  6);
    public static readonly ReadColumn Attributes     = new("Attributes",     ReadColumnType.Integer, false, 7);

    public static readonly TableReadSchema<FeatureRow> Schema = new(
        TableName: "Feature",
        Columns: [Feature, Feature_Parent, Title, Description, Display, Level, Directory_, Attributes],
        Map: row => Result<FeatureRow>.Success(new FeatureRow(
            row.String(Feature),
            row.StringOrNull(Feature_Parent),
            row.String(Title),
            row.StringOrNull(Description),
            row.Int32(Display),
            row.Int32(Level),
            row.StringOrNull(Directory_),
            row.Int32(Attributes))));
}

/// <summary>
/// Raw row returned by <see cref="FeatureComponentsSchema.Schema"/>.
/// </summary>
public sealed record FeatureComponentsRow(string Feature_, string Component_);

/// <summary>
/// Declarative read schema for the MSI <c>FeatureComponents</c> junction table.
/// Columns: Feature_ (PK1, FK→Feature), Component_ (PK2, FK→Component).
/// </summary>
public static class FeatureComponentsSchema
{
    public static readonly ReadColumn Feature_   = new("Feature_",   ReadColumnType.String, false, 0);
    public static readonly ReadColumn Component_ = new("Component_", ReadColumnType.String, false, 1);

    public static readonly TableReadSchema<FeatureComponentsRow> Schema = new(
        TableName: "FeatureComponents",
        Columns: [Feature_, Component_],
        Map: row => Result<FeatureComponentsRow>.Success(new FeatureComponentsRow(
            row.String(Feature_),
            row.String(Component_))));
}
