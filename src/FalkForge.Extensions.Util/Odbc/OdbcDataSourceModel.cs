namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDataSourceModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string DriverName { get; init; }
    public required OdbcRegistration Registration { get; init; }
    public Dictionary<string, string> Properties { get; init; } = new();
}
