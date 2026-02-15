namespace FalkForge.Decompiler;

/// <summary>
/// Abstraction over MSI database table reads, enabling testability without actual MSI files.
/// </summary>
public interface IMsiTableAccess : IDisposable
{
    /// <summary>
    /// Queries all rows from the specified table.
    /// Returns a list of string arrays, one per row, with field values in column order.
    /// </summary>
    Result<List<string?[]>> QueryTable(string tableName, string[] columns);

    /// <summary>
    /// Gets a summary information property by its property ID.
    /// </summary>
    Result<string?> GetSummaryProperty(int propertyId);

    /// <summary>
    /// Checks whether a table exists in the database.
    /// </summary>
    Result<bool> TableExists(string tableName);
}
