using FalkForge.Models;

namespace FalkForge.Builders;

// Features, launch conditions, and upgrade/downgrade policy.
public sealed partial class PackageBuilder
{
    public PackageBuilder Feature(string id, Action<FeatureBuilder> configure)
    {
        var builder = new FeatureBuilder(id);
        configure(builder);
        // Lift feature-scoped files (and nested child-feature files) into the package file list
        // before building the model — after Build() the builder is discarded.
        _files.AddRange(builder.CollectFiles());
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
