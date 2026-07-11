namespace FalkForge.Extensibility;

public interface IMsiTableContributor
{
    string TableName { get; }
    IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context);

    /// <summary>
    /// Optional read-side schema for the decompile path. When non-null, the
    /// decompiler reads this contributor's custom table and includes its rows
    /// in <c>MsiReadRecipe.ExtensionRows</c>. When null (the default), the
    /// table is silently skipped during decompile. Symmetric to the Cycle 2
    /// write-side schema on <c>ITableProducer</c>.
    /// </summary>
    ITableReadSchema? ReadSchema => null;

    /// <summary>
    /// Optional write-side schema declaring this contributor's custom-table columns.
    /// The MSI compiler uses it to issue the <c>CREATE TABLE</c> statement for a
    /// non-built-in table so the contributed <see cref="GetRows"/> rows can be inserted.
    /// <para>
    /// Required whenever <see cref="TableName"/> is <b>not</b> a built-in MSI table:
    /// a contributor that yields rows for an unknown table with a <see langword="null"/>
    /// or empty <see cref="WriteColumns"/> fails the build loudly rather than silently
    /// dropping the rows. When <see cref="TableName"/> names an existing built-in table
    /// (e.g. <c>Registry</c>, <c>CustomAction</c>), <see cref="WriteColumns"/> is ignored
    /// because the compiler already knows that table's schema and merges the rows into it.
    /// </para>
    /// Column order is authoritative and drives deterministic, reproducible column layout.
    /// </summary>
    IReadOnlyList<ContributedColumn>? WriteColumns => null;
}