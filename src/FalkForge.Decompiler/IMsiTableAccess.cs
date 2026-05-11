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
}
