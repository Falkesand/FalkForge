using FalkForge.Extensibility;

namespace FalkForge.Extensions.Driver;

public sealed class DriverTableContributor : IMsiTableContributor
{
    private readonly List<DriverModel> _drivers = [];

    public IReadOnlyList<DriverModel> Drivers => _drivers;

    public string TableName => "FalkDriverPackage";

    /// <inheritdoc/>
    public IReadOnlyList<ContributedColumn> WriteColumns { get; } =
    [
        ContributedColumn.Key("Action"),
        ContributedColumn.Int("Type"),
        ContributedColumn.Text("Source", 72),
        ContributedColumn.Text("Target"),
        ContributedColumn.Text("Condition"),
        ContributedColumn.Text("Description"),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>();

        foreach (var driver in _drivers)
        {
            var installFlags = BuildInstallFlags(driver);

            var installRow = new MsiTableRow()
                .Set("Action", $"DrvInstall_{driver.Id}")
                .Set("Type", 3090) // Deferred, no-impersonate, EXE
                .Set("Source", "pnputil")
                .Set("Target", $"/add-driver \"[INSTALLDIR]{driver.InfFilePath}\"{installFlags}")
                .Set("Condition", driver.Condition)
                .Set("Description", driver.Description);

            var uninstallRow = new MsiTableRow()
                .Set("Action", $"DrvUninstall_{driver.Id}")
                .Set("Type", 3090)
                .Set("Source", "pnputil")
                .Set("Target", $"/delete-driver \"[INSTALLDIR]{driver.InfFilePath}\" /uninstall{(driver.ForceInstall ? " /force" : string.Empty)}")
                .Set("Condition", driver.Condition)
                .Set("Description", driver.Description);

            rows.Add(installRow);
            rows.Add(uninstallRow);
        }

        return rows;
    }

    private static string BuildInstallFlags(DriverModel driver)
    {
        var flags = string.Empty;

        if (driver.PlugAndPlay)
            flags += " /install";

        if (driver.ForceInstall)
            flags += " /force";

        return flags;
    }

    public void AddDriver(DriverModel driver)
    {
        _drivers.Add(driver);
    }
}
