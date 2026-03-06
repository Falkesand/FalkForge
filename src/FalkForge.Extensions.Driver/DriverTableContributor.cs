using FalkForge.Extensibility;

namespace FalkForge.Extensions.Driver;

public sealed class DriverTableContributor : IMsiTableContributor
{
    private readonly List<DriverModel> _drivers = [];

    public IReadOnlyList<DriverModel> Drivers => _drivers;

    public string TableName => "FalkDriverPackage";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>();

        foreach (var driver in _drivers)
        {
            var forceFlag = driver.ForceInstall ? " /force" : string.Empty;

            var installRow = new MsiTableRow()
                .Set("Action", $"DrvInstall_{driver.Id}")
                .Set("Type", 3090) // Deferred, no-impersonate, EXE
                .Set("Source", "pnputil")
                .Set("Target", $"/install-driver \"[INSTALLDIR]{driver.InfFilePath}\" /subdirs{forceFlag}")
                .Set("Condition", driver.Condition);

            var uninstallRow = new MsiTableRow()
                .Set("Action", $"DrvUninstall_{driver.Id}")
                .Set("Type", 3090)
                .Set("Source", "pnputil")
                .Set("Target", $"/delete-driver \"[INSTALLDIR]{driver.InfFilePath}\" /uninstall")
                .Set("Condition", driver.Condition);

            rows.Add(installRow);
            rows.Add(uninstallRow);
        }

        return rows;
    }

    public void AddDriver(DriverModel driver)
    {
        _drivers.Add(driver);
    }
}
