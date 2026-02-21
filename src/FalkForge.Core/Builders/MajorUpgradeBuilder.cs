namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class MajorUpgradeBuilder
{
    private bool _allowSameVersionUpgrades;
    private RemoveExistingProductsSchedule _schedule = RemoveExistingProductsSchedule.AfterInstallValidate;
    private bool _migrateFeatures = true;

    public MajorUpgradeBuilder AllowSameVersionUpgrades()
    {
        _allowSameVersionUpgrades = true;
        return this;
    }

    public MajorUpgradeBuilder Schedule(RemoveExistingProductsSchedule schedule)
    {
        _schedule = schedule;
        return this;
    }

    public MajorUpgradeBuilder MigrateFeatures(bool migrate)
    {
        _migrateFeatures = migrate;
        return this;
    }

    internal MajorUpgradeModel Build() => new()
    {
        AllowSameVersionUpgrades = _allowSameVersionUpgrades,
        Schedule = _schedule,
        MigrateFeatures = _migrateFeatures
    };
}
