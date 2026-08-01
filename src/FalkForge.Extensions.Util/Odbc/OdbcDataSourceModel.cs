namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDataSourceModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string DriverName { get; init; }
    public required OdbcRegistration Registration { get; init; }

    /// <summary>
    /// External key into the MSI Component table. Required by the real ODBCDataSource schema's
    /// non-nullable Component_ column: the compiler fails the build loudly when it is left unset,
    /// rather than emitting an invalid ODBCDataSource row.
    /// </summary>
    public string? ComponentRef { get; init; }

    public Dictionary<string, string> Properties { get; init; } = new();
}
