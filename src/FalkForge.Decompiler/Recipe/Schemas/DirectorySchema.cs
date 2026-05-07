using System.Collections.Immutable;

namespace FalkForge.Decompiler.Recipe.Schemas;

/// <summary>
/// Raw row returned by <see cref="DirectorySchema.Schema"/>.
/// </summary>
public sealed record DirectoryRow(string Directory, string? Directory_Parent, string DefaultDir);

/// <summary>
/// Declarative read schema for the MSI <c>Directory</c> table.
/// Columns: Directory (PK), Directory_Parent (nullable FK), DefaultDir.
/// </summary>
public static class DirectorySchema
{
    public static readonly ReadColumn Directory        = new("Directory",        ReadColumnType.String, false, 0);
    public static readonly ReadColumn Directory_Parent = new("Directory_Parent", ReadColumnType.String, true,  1);
    public static readonly ReadColumn DefaultDir       = new("DefaultDir",       ReadColumnType.String, false, 2);

    public static readonly TableReadSchema<DirectoryRow> Schema = new(
        TableName: "Directory",
        Columns: [Directory, Directory_Parent, DefaultDir],
        Map: row => Result<DirectoryRow>.Success(new DirectoryRow(
            row.String(Directory),
            row.StringOrNull(Directory_Parent),
            row.String(DefaultDir))));
}
