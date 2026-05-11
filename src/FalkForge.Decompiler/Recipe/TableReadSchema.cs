using System.Collections.Immutable;
using FalkForge.Extensibility;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Delegate type for mapping a single <see cref="ReadRow"/> into a typed domain value.
/// </summary>
public delegate Result<TRow> RowMapper<TRow>(ReadRow row);

/// <summary>
/// Immutable description of how to read one MSI table into a list of typed rows.
/// One static readonly field per table in a per-area schema class. Declares
/// column schema, error diagnostic code, and a pure row mapper delegate.
/// Symmetric to Cycle 2's <c>ITableProducer</c> on the build side.
/// <para>
/// Implements <see cref="ITableReadSchema"/> so extension contributors can return
/// a typed schema from <see cref="IMsiTableContributor.ReadSchema"/> and the
/// decompiler can drive it via the erased interface without knowing <typeparamref name="TRow"/>.
/// </para>
/// </summary>
public sealed record TableReadSchema<TRow>(
    string TableName,
    ImmutableArray<ReadColumn> Columns,
    RowMapper<TRow> Map,
    string DiagnosticCode = "DEC003") : ITableReadSchema
{
    /// <inheritdoc/>
    Result<IReadOnlyList<object>> ITableReadSchema.ReadErased(ITableQuery query)
    {
        var result = TableReadEngine.ReadOne(this, query);
        if (result.IsFailure)
            return Result<IReadOnlyList<object>>.Failure(result.Error);

        // Box each typed row as object — allocation is acceptable here because
        // extension tables are small and this path runs once per decompile.
        IReadOnlyList<object> boxed = result.Value.Cast<object>().ToList();
        return Result<IReadOnlyList<object>>.Success(boxed);
    }
}
