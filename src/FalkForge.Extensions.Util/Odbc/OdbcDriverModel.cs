namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDriverModel
{
    public required string Id { get; init; }
    public required string DriverName { get; init; }
    public required string FileName { get; init; }
    public string? SetupFileName { get; init; }
}
