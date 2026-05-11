namespace FalkForge.Extensibility;

/// <summary>
/// Minimal read abstraction over an MSI table store. Implemented by
/// <c>FalkForge.Decompiler.IMsiTableAccess</c>. Defined here so extension
/// <see cref="ITableReadSchema"/> implementations can read tables without
/// referencing the Decompiler assembly.
/// </summary>
public interface ITableQuery
{
    /// <summary>Returns true when the named table exists in the MSI database.</summary>
    Result<bool> TableExists(string tableName);

    /// <summary>
    /// Queries all rows from <paramref name="tableName"/>, returning the requested
    /// <paramref name="columns"/> in declaration order. Returns a failure when the
    /// table query fails; returns an empty list when the table is absent.
    /// </summary>
    Result<List<string?[]>> QueryTable(string tableName, string[] columns);
}
