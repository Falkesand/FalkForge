using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class MajorUpgradeBuilder
{
    private bool _allowSameVersionUpgrades;
    private bool _migrateFeatures = true;
    private RemoveExistingProductsSchedule _schedule = RemoveExistingProductsSchedule.AfterInstallValidate;

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

    internal MajorUpgradeModel Build()
    {
        return new MajorUpgradeModel
        {
            AllowSameVersionUpgrades = _allowSameVersionUpgrades,
            Schedule = _schedule,
            MigrateFeatures = _migrateFeatures
        };
    }
}