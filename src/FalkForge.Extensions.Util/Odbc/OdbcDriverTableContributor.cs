using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDriverTableContributor : IMsiTableContributor
{
    private readonly List<OdbcDriverModel> _drivers = [];

    public string TableName => "ODBCDriver";

    public void Add(OdbcDriverModel driver) => _drivers.Add(driver);

    public IReadOnlyList<OdbcDriverModel> Drivers => _drivers;

    /// <inheritdoc/>
    /// <remarks>
    /// Matches the real ODBCDriver schema: Driver (key), Component_, Description and File_ are all
    /// non-nullable; only File_Setup is optional. Component_ is non-nullable because a driver with
    /// no owning component is never installed by the MSI engine — leaving it unset must fail the
    /// build loudly, not ship a dead table.
    /// </remarks>
    public IReadOnlyList<ContributedColumn> WriteColumns { get; } =
    [
        ContributedColumn.Key("Driver"),
        ContributedColumn.Text("Component_", 72, nullable: false),
        ContributedColumn.Text("Description", nullable: false),
        ContributedColumn.Text("File_", 72, nullable: false),
        ContributedColumn.Text("File_Setup", 72, nullable: true),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>(_drivers.Count);

        foreach (var driver in _drivers)
        {
            var row = new MsiTableRow()
                .Set("Driver", driver.Id)
                .Set("Component_", driver.ComponentRef)
                .Set("Description", driver.DriverName)
                .Set("File_", driver.FileName)
                .Set("File_Setup", driver.SetupFileName);

            rows.Add(row);
        }

        return rows;
    }
}
