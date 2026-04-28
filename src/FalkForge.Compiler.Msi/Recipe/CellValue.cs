namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Discriminated union of cell values that may appear in a
/// <see cref="RecipeRow"/>. Each subtype maps directly to one MSI column
/// representation: <see cref="Null"/> for nullable columns left empty,
/// <see cref="IntValue"/> for integer columns, <see cref="StringValue"/>
/// for plain string columns, <see cref="ForeignKey"/> for primary-key
/// references into another table (used by FK validation), and
/// <see cref="StreamRef"/> for binary cells whose bytes live in
/// <see cref="MsiDatabaseRecipe.Streams"/>.
/// </summary>
public abstract record CellValue
{
    private CellValue()
    {
    }

    /// <summary>Empty cell. Allowed only for columns with <c>Nullable = true</c>.</summary>
    public sealed record Null : CellValue;

    /// <summary>Integer cell.</summary>
    public sealed record IntValue(int Value) : CellValue;

    /// <summary>String cell. Plain (non-foreign-key) string content.</summary>
    public sealed record StringValue(string Value) : CellValue;

    /// <summary>
    /// Foreign-key reference into <paramref name="TargetTable"/>'s primary key
    /// <paramref name="TargetKey"/>. Used by recipe-level FK validators.
    /// </summary>
    public sealed record ForeignKey(TableId TargetTable, string TargetKey) : CellValue;

    /// <summary>Reference to a stream by name. Bytes live in <see cref="MsiDatabaseRecipe.Streams"/>.</summary>
    public sealed record StreamRef(string StreamName) : CellValue;
}
