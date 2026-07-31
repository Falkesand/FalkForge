using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Minimal <see cref="IMsiTableContributor"/> over an already-materialized row set that targets a
/// built-in MSI table (so no write schema is needed — the compiler owns that table's columns).
/// Shared by <see cref="ExecutionStepEmitter"/> (the synthetic <c>CustomAction</c> /
/// <c>InstallExecuteSequence</c> rows) and <see cref="HiddenPropertiesEmitter"/> (the single merged
/// <c>MsiHiddenProperties</c> row) — two independent owners now, so it lives in its own file rather
/// than as a nested type of either.
/// </summary>
internal sealed class StaticTableContributor(string tableName, IReadOnlyList<MsiTableRow> rows)
    : IMsiTableContributor
{
    public string TableName => tableName;

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context) => rows;
}
