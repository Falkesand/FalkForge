namespace FalkInstaller.Models;

public sealed class MajorUpgradeModel
{
    public bool AllowDowngrades { get; init; }
    public bool AllowSameVersionUpgrades { get; init; }
    public string? DowngradeErrorMessage { get; init; }
    public RemoveExistingProductsSchedule Schedule { get; init; } = RemoveExistingProductsSchedule.AfterInstallValidate;
    public bool MigrateFeatures { get; init; } = true;
}
