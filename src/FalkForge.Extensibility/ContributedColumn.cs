namespace FalkForge.Extensibility;

/// <summary>
/// Storage class of a column declared by an <see cref="IMsiTableContributor"/>
/// write-side schema (<see cref="IMsiTableContributor.WriteColumns"/>). Mirrors
/// the MSI column storage classes the compiler understands.
/// </summary>
public enum ContributedColumnType
{
    /// <summary>Text column, stored as a length-bounded <c>CHAR</c> in the MSI string pool.</summary>
    String,

    /// <summary>16-bit integer column (<c>SHORT</c>).</summary>
    Int16,

    /// <summary>32-bit integer column (<c>LONG</c>).</summary>
    Int32,

    /// <summary>Binary stream column (<c>OBJECT</c>). The cell value must be a <c>byte[]</c>.</summary>
    Binary,
}

/// <summary>
/// Write-side schema descriptor for one column of a custom MSI table contributed by an
/// <see cref="IMsiTableContributor"/>. A contributor that targets a table which is not a
/// built-in MSI table (e.g. <c>SqlDatabase</c>, <c>WixFirewallException</c>) must declare
/// its columns via <see cref="IMsiTableContributor.WriteColumns"/> so the compiler can
/// issue the <c>CREATE TABLE</c> statement. Column declaration order is authoritative and
/// drives both schema emission and deterministic (reproducible-build) column ordering.
/// </summary>
public sealed class ContributedColumn
{
    /// <summary>
    /// Column identifier. Must satisfy the MSI identifier grammar
    /// (<c>^[A-Za-z_][A-Za-z0-9_]{0,30}$</c>); the compiler re-validates it as
    /// defense-in-depth before it reaches SQL emission.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Storage class of the column.</summary>
    public required ContributedColumnType Type { get; init; }

    /// <summary>
    /// Maximum character width for <see cref="ContributedColumnType.String"/> columns.
    /// Ignored for integer and binary columns. Defaults to 255 when left at zero.
    /// </summary>
    public int Width { get; init; }

    /// <summary>Whether the column accepts a null cell. Primary-key columns must be non-nullable.</summary>
    public bool Nullable { get; init; }

    /// <summary>Whether this column participates in the table's primary key.</summary>
    public bool PrimaryKey { get; init; }
}
