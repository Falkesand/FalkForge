namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Zero-allocation view over one raw row from <see cref="IMsiTableAccess.QueryTable"/>.
/// Column access is type-safe via <see cref="ReadColumn"/> tokens — no <c>row[0]</c>,
/// no stringly-typed lookups. Throws <see cref="InvalidOperationException"/> for
/// type/nullability violations so callers get a structured error at the engine level.
/// Not a ref struct (ValueTuple closures in mapper delegates prevent ref struct use),
/// but instances are short-lived and not retained beyond the mapper call.
/// </summary>
public sealed class ReadRow
{
    private readonly string?[] _cells;
    private readonly string _tableName;
    private readonly int _rowIndex;

    internal ReadRow(string?[] cells, string tableName, int rowIndex)
    {
        _cells = cells;
        _tableName = tableName;
        _rowIndex = rowIndex;
    }

    /// <summary>Returns the non-null string value at <paramref name="col"/>.</summary>
    public string String(ReadColumn col)
    {
        var raw = _cells[col.Index];
        return raw ?? string.Empty;
    }

    /// <summary>Returns the nullable string value at <paramref name="col"/>.</summary>
    public string? StringOrNull(ReadColumn col) => _cells[col.Index];

    /// <summary>
    /// Parses the cell at <paramref name="col"/> as a 32-bit signed integer.
    /// Throws if the cell cannot be parsed.
    /// </summary>
    public int Int32(ReadColumn col)
    {
        var raw = _cells[col.Index];
        if (!int.TryParse(raw, out var value))
            throw new FormatException(
                $"DEC003: Table '{_tableName}' row {_rowIndex} column '{col.Name}' " +
                $"value '{raw}' is not a valid integer.");
        return value;
    }

    /// <summary>
    /// Parses the cell at <paramref name="col"/> as a nullable 32-bit signed integer.
    /// Returns null for null/empty cells; throws if the cell is non-null but unparseable.
    /// </summary>
    public int? Int32OrNull(ReadColumn col)
    {
        var raw = _cells[col.Index];
        if (string.IsNullOrEmpty(raw))
            return null;
        if (!int.TryParse(raw, out var value))
            throw new FormatException(
                $"DEC003: Table '{_tableName}' row {_rowIndex} column '{col.Name}' " +
                $"value '{raw}' is not a valid integer.");
        return value;
    }

    /// <summary>Internal: cell count for shape validation.</summary>
    internal int CellCount => _cells.Length;
}
