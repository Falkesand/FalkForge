using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Immutable description of a single MSI database table: identifier, schema,
/// rows, primary-key column indices, declared foreign-key relationships, and
/// the precomputed SQL strings the executor will issue to <c>msi.dll</c>.
/// <see cref="ForeignKeys"/> is structural metadata copied from the producer's
/// <see cref="TableSchema.ForeignKeys"/> and powers
/// <see cref="ForeignKeyValidator"/>; it is not enforced by <c>msi.dll</c> at
/// insert time. The field has a default of an empty array so existing
/// construction sites that do not yet set it remain source-compatible.
/// </summary>
public sealed record RecipeTable
{
    public required TableId Name { get; init; }
    public required ImmutableArray<RecipeColumn> Columns { get; init; }
    public required ImmutableArray<RecipeRow> Rows { get; init; }
    public required ImmutableArray<ColumnIndex> PrimaryKey { get; init; }
    public required string CreateTableSql { get; init; }
    public required string InsertViewSql { get; init; }

    /// <summary>
    /// Foreign-key declarations originating from columns of this table.
    /// Defaults to an empty array.
    /// </summary>
    public ImmutableArray<ForeignKeySpec> ForeignKeys { get; init; } = ImmutableArray<ForeignKeySpec>.Empty;
}
