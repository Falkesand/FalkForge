namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Declarative description of a foreign-key relationship from one column of a
/// <see cref="TableSchema"/> to another table identified by <see cref="TargetTable"/>.
/// The relationship is structural metadata only — it is not enforced by
/// <c>msi.dll</c> at insert time. Equality is record-default: two specs match
/// when both <see cref="SourceColumn"/> and <see cref="TargetTable"/> match.
/// </summary>
public sealed record ForeignKeySpec
{
    /// <summary>Index of the column on the owning <see cref="TableSchema"/> that holds the FK value.</summary>
    public required ColumnIndex SourceColumn { get; init; }

    /// <summary>Identifier of the table referenced by <see cref="SourceColumn"/>.</summary>
    public required TableId TargetTable { get; init; }
}
