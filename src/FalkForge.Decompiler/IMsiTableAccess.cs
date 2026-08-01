using FalkForge.Extensibility;

namespace FalkForge.Decompiler;

/// <summary>
/// Abstraction over MSI database table reads, enabling testability without actual MSI files.
/// Extends <see cref="ITableQuery"/> (defined in Extensibility) so extension
/// <see cref="ITableReadSchema"/> implementations can read tables without referencing
/// the Decompiler assembly directly. Implementors satisfy <see cref="ITableQuery"/>
/// members (<see cref="ITableQuery.QueryTable"/> and <see cref="ITableQuery.TableExists"/>)
/// and additionally expose <see cref="GetSummaryProperty"/>.
/// </summary>
public interface IMsiTableAccess : IDisposable, ITableQuery
{
    /// <summary>
    /// Gets a summary information property by its property ID.
    /// </summary>
    Result<string?> GetSummaryProperty(int propertyId);

    /// <summary>
    /// Returns the names of every table present in the MSI database (queried from the
    /// <c>_Tables</c> system catalog), including MSI-internal catalog tables (e.g.
    /// <c>_Tables</c>, <c>_Columns</c>) and any custom/extension tables. Callers that only
    /// want "real" tables should filter names starting with <c>_</c> themselves.
    /// </summary>
    Result<IReadOnlyList<string>> GetTableNames();
}
