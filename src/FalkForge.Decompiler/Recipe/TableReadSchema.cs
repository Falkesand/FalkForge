using System.Collections.Immutable;

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
/// </summary>
public sealed record TableReadSchema<TRow>(
    string TableName,
    ImmutableArray<ReadColumn> Columns,
    RowMapper<TRow> Map,
    string DiagnosticCode = "DEC003");
