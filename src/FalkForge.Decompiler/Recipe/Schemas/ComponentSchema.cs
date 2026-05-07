using System.Collections.Immutable;

namespace FalkForge.Decompiler.Recipe.Schemas;

/// <summary>
/// Raw row returned by <see cref="ComponentSchema.Schema"/>.
/// </summary>
public sealed record ComponentRow(
    string Component,
    string? ComponentId,
    string Directory_,
    int    Attributes,
    string? Condition,
    string? KeyPath);

/// <summary>
/// Declarative read schema for the MSI <c>Component</c> table.
/// Columns: Component (PK), ComponentId, Directory_, Attributes, Condition, KeyPath.
/// </summary>
public static class ComponentSchema
{
    public static readonly ReadColumn Component   = new("Component",   ReadColumnType.String,  false, 0);
    public static readonly ReadColumn ComponentId = new("ComponentId", ReadColumnType.String,  true,  1);
    public static readonly ReadColumn Directory_  = new("Directory_",  ReadColumnType.String,  false, 2);
    public static readonly ReadColumn Attributes  = new("Attributes",  ReadColumnType.Integer, false, 3);
    public static readonly ReadColumn Condition   = new("Condition",   ReadColumnType.String,  true,  4);
    public static readonly ReadColumn KeyPath     = new("KeyPath",     ReadColumnType.String,  true,  5);

    public static readonly TableReadSchema<ComponentRow> Schema = new(
        TableName: "Component",
        Columns: [Component, ComponentId, Directory_, Attributes, Condition, KeyPath],
        Map: row => Result<ComponentRow>.Success(new ComponentRow(
            row.String(Component),
            row.StringOrNull(ComponentId),
            row.String(Directory_),
            row.Int32(Attributes),
            row.StringOrNull(Condition),
            row.StringOrNull(KeyPath))));
}
