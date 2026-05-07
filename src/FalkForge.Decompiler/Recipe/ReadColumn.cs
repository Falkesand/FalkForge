namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Immutable descriptor for one column in a <see cref="TableReadSchema{TRow}"/>.
/// Pairs a column name and type with the positional index used to extract the
/// cell from a raw row. Stating the index explicitly lets schemas declare columns
/// out of positional order and prevents silent copy-paste index swaps.
/// </summary>
public readonly record struct ReadColumn(
    string Name,
    ReadColumnType Type,
    bool Nullable,
    int Index);
