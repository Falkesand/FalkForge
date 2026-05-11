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
}