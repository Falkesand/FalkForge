namespace FalkForge.Compiler.Bundle.Builders;

using FalkForge.Engine.Protocol.Manifest;

public sealed class NestedBundlePackageBuilder
{
    private readonly ChainPackageOptions _chainOptions = new();
    private readonly string _sourcePath;
    private string _displayName;
    private string _id;
    private string? _installCondition;
    private bool _vital = true;

    internal NestedBundlePackageBuilder(string sourcePath)
    {
        _sourcePath = sourcePath;
        _id = Path.GetFileNameWithoutExtension(sourcePath);
        _displayName = _id;
    }

    public NestedBundlePackageBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public NestedBundlePackageBuilder DisplayName(string name)
    {
        _displayName = name;
        return this;
    }

    public NestedBundlePackageBuilder Vital(bool vital)
    {
        _vital = vital;
        return this;
    }

    public NestedBundlePackageBuilder InstallCondition(string condition)
    {
        _installCondition = condition;
        return this;
    }

    public NestedBundlePackageBuilder InstallCondition(Condition condition)
    {
        return InstallCondition(condition.ToString());
    }

    public NestedBundlePackageBuilder Container(string containerId)
    {
        _chainOptions.SetContainer(containerId);
        return this;
    }

    public NestedBundlePackageBuilder Container(ContainerRef containerRef)
    {
        _chainOptions.SetContainer(containerRef);
        return this;
    }

    public NestedBundlePackageBuilder RemotePayload(string url, string sha256, long size)
    {
        _chainOptions.SetRemotePayload(url, sha256, size);
        return this;
    }

    public NestedBundlePackageBuilder ExitCode(int code, ExitCodeBehavior behavior)
    {
        _chainOptions.SetExitCode(code, behavior);
        return this;
    }

    public NestedBundlePackageBuilder DetectionMode(DetectionMode mode)
    {
        _chainOptions.SetDetectionMode(mode);
        return this;
    }

    public NestedBundlePackageBuilder SearchCondition(Action<SearchConditionBuilder> configure)
    {
        _chainOptions.AddSearchCondition(configure);
        return this;
    }

    public NestedBundlePackageBuilder Permanent(bool permanent = true)
    {
        _chainOptions.SetPermanent(permanent);
        return this;
    }

    internal BundlePackageModel Build()
    {
        return new BundlePackageModel
        {
            Id = _id,
            Type = BundlePackageType.BundlePackage,
            DisplayName = _displayName,
            Vital = _vital,
            SourcePath = _sourcePath,
            InstallCondition = _installCondition,
            ContainerId = _chainOptions.ContainerId,
            RemotePayload = _chainOptions.RemotePayload,
            ExitCodes = new Dictionary<int, ExitCodeBehavior>(_chainOptions.ExitCodes),
            DetectionMode = _chainOptions.DetectionMode,
            SearchConditions = _chainOptions.SearchConditions.ToArray(),
            Permanent = _chainOptions.Permanent
        };
    }
}
