using System.Collections.Immutable;

namespace FalkForge.Decompiler.Recipe.Schemas;

/// <summary>
/// Raw row returned by <see cref="RegistrySchema.Schema"/>.
/// </summary>
public sealed record RegistryRow(
    string  Registry,
    int     Root,
    string  Key,
    string? Name,
    string? Value,
    string? Component_);

/// <summary>
/// Declarative read schema for the MSI <c>Registry</c> table.
/// Columns: Registry (PK), Root (int), Key, Name (nullable), Value (nullable), Component_.
/// </summary>
public static class RegistrySchema
{
    public static readonly ReadColumn Registry   = new("Registry",   ReadColumnType.String,  false, 0);
    public static readonly ReadColumn Root       = new("Root",       ReadColumnType.Integer, false, 1);
    public static readonly ReadColumn Key        = new("Key",        ReadColumnType.String,  false, 2);
    public static readonly ReadColumn Name       = new("Name",       ReadColumnType.String,  true,  3);
    public static readonly ReadColumn Value      = new("Value",      ReadColumnType.String,  true,  4);
    public static readonly ReadColumn Component_ = new("Component_", ReadColumnType.String,  true,  5);

    public static readonly TableReadSchema<RegistryRow> Schema = new(
        TableName: "Registry",
        Columns: [Registry, Root, Key, Name, Value, Component_],
        Map: row => Result<RegistryRow>.Success(new RegistryRow(
            row.String(Registry),
            row.Int32(Root),
            row.String(Key),
            row.StringOrNull(Name),
            row.StringOrNull(Value),
            row.StringOrNull(Component_))));
}
