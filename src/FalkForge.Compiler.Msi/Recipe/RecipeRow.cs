using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Immutable single row of a <see cref="RecipeTable"/>. Cells are stored
/// in column order matching the parent table's <see cref="RecipeTable.Columns"/>.
/// </summary>
public sealed record RecipeRow
{
    public required ImmutableArray<CellValue> Cells { get; init; }
}
