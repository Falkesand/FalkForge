using System.Collections.Immutable;

namespace FalkForge.Decompiler.Recipe.Schemas;

/// <summary>
/// Raw row returned by <see cref="PropertySchema.Schema"/>.
/// Carries every cell as read from the MSI Property table before any
/// domain-level filtering (e.g. internal-property suppression).
/// </summary>
public sealed record PropertyRow(string Property, string Value);

/// <summary>
/// Declarative read schema for the MSI <c>Property</c> table.
/// Columns: Property (PK, string), Value (string).
/// </summary>
public static class PropertySchema
{
    public static readonly ReadColumn Property = new("Property", ReadColumnType.String, false, 0);
    public static readonly ReadColumn Value    = new("Value",    ReadColumnType.String, false, 1);

    public static readonly TableReadSchema<PropertyRow> Schema = new(
        TableName: "Property",
        Columns: [Property, Value],
        Map: row => Result<PropertyRow>.Success(new PropertyRow(
            row.String(Property),
            row.String(Value))));
}
