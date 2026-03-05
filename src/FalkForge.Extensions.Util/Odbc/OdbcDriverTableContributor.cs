using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDriverTableContributor : IMsiTableContributor
{
    private readonly List<OdbcDriverModel> _drivers = [];

    public string TableName => "ODBCDriver";

    public void Add(OdbcDriverModel driver) => _drivers.Add(driver);

    public IReadOnlyList<OdbcDriverModel> Drivers => _drivers;

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>(_drivers.Count);

        foreach (var driver in _drivers)
        {
            var row = new MsiTableRow()
                .Set("Driver", driver.Id)
                .Set("Description", driver.DriverName)
                .Set("File_", driver.FileName)
                .Set("File_Setup", driver.SetupFileName);

            rows.Add(row);
        }

        return rows;
    }
}
