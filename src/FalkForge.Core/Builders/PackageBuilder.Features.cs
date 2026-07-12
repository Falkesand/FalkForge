using FalkForge.Models;

namespace FalkForge.Builders;

// Features, launch conditions, and upgrade/downgrade policy.
public sealed partial class PackageBuilder
{
    public PackageBuilder Feature(string id, Action<FeatureBuilder> configure)
    {
        var builder = new FeatureBuilder(id);
        configure(builder);
        // Lift feature-scoped files/services/registry entries/shortcuts/environment
        // variables/fonts/INI files/permissions/file associations (and nested child-feature
        // ones) into the package's flat lists before building the model — after Build() the
        // builder is discarded.
        _files.AddRange(builder.CollectFiles());
        _services.AddRange(builder.CollectServices());
        _registryEntries.AddRange(builder.CollectRegistryEntries());
        _shortcuts.AddRange(builder.CollectShortcuts());
        _environmentVariables.AddRange(builder.CollectEnvironmentVariables());
        _fonts.AddRange(builder.CollectFonts());
        _iniFiles.AddRange(builder.CollectIniFiles());
        _permissions.AddRange(builder.CollectPermissions());
        _fileAssociations.AddRange(builder.CollectFileAssociations());
        _features.Add(builder.Build());
        return this;
    }

    public PackageBuilder Require(string condition, string message)
    {
        _launchConditions.Add(new LaunchConditionModel { Condition = condition, Message = message });
        return this;
    }

    public PackageBuilder Require(Condition condition, string message)
    {
        return Require(condition.ToString(), message);
    }

    public PackageBuilder Upgrade(Action<UpgradeBuilder> configure)
    {
        var builder = new UpgradeBuilder();
        configure(builder);
        _upgrade = builder.Build();
        return this;
    }

    public PackageBuilder MajorUpgrade(Action<MajorUpgradeBuilder> configure)
    {
        var builder = new MajorUpgradeBuilder();
        configure(builder);
        _majorUpgrade = builder.Build();
        return this;
    }

    public PackageBuilder Downgrade(Action<DowngradeBuilder> configure)
    {
        var builder = new DowngradeBuilder();
        configure(builder);
        _downgrade = builder.Build();
        return this;
    }
}
