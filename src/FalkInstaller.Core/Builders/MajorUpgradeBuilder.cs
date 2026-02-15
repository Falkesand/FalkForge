namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class MajorUpgradeBuilder
{
    private bool _allowDowngrades;
    private bool _allowSameVersionUpgrades;
    private string? _downgradeErrorMessage;
    private RemoveExistingProductsSchedule _schedule = RemoveExistingProductsSchedule.AfterInstallValidate;
    private bool _migrateFeatures = true;

    public MajorUpgradeBuilder AllowDowngrades()
    {
        _allowDowngrades = true;
        return this;
    }

    public MajorUpgradeBuilder AllowSameVersionUpgrades()
    {
        _allowSameVersionUpgrades = true;
        return this;
    }

    public MajorUpgradeBuilder DowngradeErrorMessage(string message)
    {
        _downgradeErrorMessage = message;
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
        AllowDowngrades = _allowDowngrades,
        AllowSameVersionUpgrades = _allowSameVersionUpgrades,
        DowngradeErrorMessage = _downgradeErrorMessage,
        Schedule = _schedule,
        MigrateFeatures = _migrateFeatures
    };
}
