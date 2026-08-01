namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDataSourceModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// Description of the driver this data source uses. When it matches the
    /// <see cref="OdbcDriverModel.DriverName"/> of a driver the same package installs, the compiler
    /// attaches the data source to that driver's component so the two are removed together.
    /// </summary>
    public required string DriverName { get; init; }

    public required OdbcRegistration Registration { get; init; }

    public Dictionary<string, string> Properties { get; init; } = new();
}
