using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Immutable description of a single MSI database table: identifier, schema,
/// rows, primary-key column indices, and the precomputed SQL strings the
/// executor will issue to <c>msi.dll</c>. Foreign-key relationships are not
/// modeled here (they live on the per-table <c>TableSchema</c> introduced in
/// later phases) — this type is the pure data backbone the recipe builder
/// fills in.
/// </summary>
public sealed record RecipeTable
{
    public required TableId Name { get; init; }
    public required ImmutableArray<RecipeColumn> Columns { get; init; }
    public required ImmutableArray<RecipeRow> Rows { get; init; }
    public required ImmutableArray<ColumnIndex> PrimaryKey { get; init; }
    public required string CreateTableSql { get; init; }
    public required string InsertViewSql { get; init; }
}
