namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDriverModel
{
    public required string Id { get; init; }
    public required string DriverName { get; init; }
    public required string FileName { get; init; }
    public string? SetupFileName { get; init; }

    /// <summary>
    /// External key into the MSI Component table. Required by the real ODBCDriver schema's
    /// non-nullable Component_ column: the compiler fails the build loudly when it is left unset,
    /// rather than emitting an invalid ODBCDriver row.
    /// </summary>
    public string? ComponentRef { get; init; }
}
