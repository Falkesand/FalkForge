namespace FalkForge.Extensibility;

/// <summary>
/// Marker interface for a read-side table schema contributed by an
/// <see cref="IMsiTableContributor"/>. Implemented by
/// <c>FalkForge.Decompiler.Recipe.TableReadSchema&lt;TRow&gt;</c>.
/// <para>
/// The erased <see cref="ReadErased"/> method lets the decompiler drive any
/// schema without knowing the concrete row type at compile time.
/// </para>
/// </summary>
public interface ITableReadSchema
{
    /// <summary>Name of the MSI table this schema reads.</summary>
    string TableName { get; }

    /// <summary>
    /// Reads all rows from <paramref name="access"/> and returns them boxed as
    /// <see cref="object"/>. Returns an empty list when the table does not exist.
    /// Returns a structured failure on query or shape errors.
    /// </summary>
    Result<IReadOnlyList<object>> ReadErased(object access);
}
