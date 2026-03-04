namespace FalkForge.Models;

public sealed class MajorUpgradeModel
{
    public bool AllowSameVersionUpgrades { get; init; }
    public RemoveExistingProductsSchedule Schedule { get; init; } = RemoveExistingProductsSchedule.AfterInstallValidate;
    public bool MigrateFeatures { get; init; } = true;
}