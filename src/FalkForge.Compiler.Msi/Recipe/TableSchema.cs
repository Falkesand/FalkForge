using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Immutable schema description for a single MSI table: identifier, ordered
/// columns, primary-key column indices, and declared foreign-key relationships.
/// Validation runs at construction (and after any <c>with</c>-expression copy)
/// from the <see cref="ForeignKeys"/> init accessor — by the time it runs, all
/// other required members have been assigned in declaration order, so the
/// cross-property checks have full context. Invalid shapes throw
/// <see cref="ArgumentException"/> or <see cref="ArgumentOutOfRangeException"/>
/// rather than returning a <c>Result</c>: a <see cref="TableSchema"/> instance
/// is by construction well-formed.
/// </summary>
public sealed record TableSchema
{
    private readonly ImmutableArray<ForeignKeySpec> _foreignKeys;

    /// <summary>Identifier of the table this schema describes.</summary>
    public required TableId Name { get; init; }

    /// <summary>Ordered list of column descriptors. Must be non-empty and have unique names.</summary>
    public required ImmutableArray<RecipeColumn> Columns { get; init; }

    /// <summary>Distinct column indices that compose the primary key. Must be non-empty and in range.</summary>
    public required ImmutableArray<ColumnIndex> PrimaryKey { get; init; }

    /// <summary>Foreign-key declarations originating from columns of this schema. May be empty.</summary>
    public required ImmutableArray<ForeignKeySpec> ForeignKeys
    {
        get => _foreignKeys;
        init
        {
            _foreignKeys = value;
            Validate();
        }
    }

    /// <summary>
    /// When <see langword="true"/> (the default), the recipe builder emits the
    /// table even if the producer returns zero rows. When <see langword="false"/>,
    /// the builder suppresses both the <c>CREATE TABLE</c> statement and the
    /// <see cref="RecipeTable"/> entry for this table whenever its row output is
    /// empty — matching the legacy <see cref="Tables.TableEmitter"/> behaviour
    /// for opt-in tables such as <c>LockPermissions</c> and
    /// <c>MsiLockPermissionsEx</c>, which are only created when at least one
    /// matching permission entry exists.
    /// </summary>
    public bool EmitWhenEmpty { get; init; } = true;

    private void Validate()
    {
        if (Columns.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Columns must contain at least one column.");
        }

        if (PrimaryKey.IsDefaultOrEmpty)
        {
            throw new ArgumentException("PrimaryKey must contain at least one column index.");
        }

        // Column-name uniqueness (case-sensitive). Done before PK range checks so
        // a duplicate-name diagnostic is preferred over an index-range one when
        // both apply.
        HashSet<string> seenNames = new(Columns.Length, StringComparer.Ordinal);
        foreach (RecipeColumn column in Columns)
        {
            if (!seenNames.Add(column.Name))
            {
                throw new ArgumentException(
                    $"Columns must have unique names; duplicate name '{column.Name}'.");
            }
        }

        // PrimaryKey index range + distinctness.
        HashSet<int> seenPkIndices = new(PrimaryKey.Length);
        foreach (ColumnIndex pk in PrimaryKey)
        {
            if (pk.Value >= Columns.Length)
            {
                throw new ArgumentOutOfRangeException(
                    $"PrimaryKey index {pk.Value} is out of range for a table with {Columns.Length} columns.");
            }

            if (!seenPkIndices.Add(pk.Value))
            {
                throw new ArgumentException(
                    $"PrimaryKey indices must be distinct; duplicate index {pk.Value}.");
            }
        }

        // Foreign-key source column range. Empty FK array is fine.
        if (!_foreignKeys.IsDefault)
        {
            foreach (ForeignKeySpec fk in _foreignKeys)
            {
                if (fk.SourceColumn.Value >= Columns.Length)
                {
                    throw new ArgumentOutOfRangeException(
                        $"ForeignKeys source column index {fk.SourceColumn.Value} is out of range for a table with {Columns.Length} columns.");
                }
            }
        }
    }
}
