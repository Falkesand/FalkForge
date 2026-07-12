namespace FalkForge.Compiler.Bundle.Builders;

using FalkForge.Engine.Protocol.Manifest;

public sealed class MsuPackageBuilder
{
    private readonly ChainPackageOptions _chainOptions = new();
    private readonly string _sourcePath;
    private string _displayName;
    private string _id;
    private string? _installCondition;
    private string? _kbArticle;
    private bool _vital = true;

    internal MsuPackageBuilder(string sourcePath)
    {
        _sourcePath = sourcePath;
        _id = Path.GetFileNameWithoutExtension(sourcePath);
        _displayName = _id;
    }

    public MsuPackageBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public MsuPackageBuilder DisplayName(string name)
    {
        _displayName = name;
        return this;
    }

    public MsuPackageBuilder Vital(bool vital)
    {
        _vital = vital;
        return this;
    }

    public MsuPackageBuilder KbArticle(string kbArticle)
    {
        _kbArticle = kbArticle;
        return this;
    }

    public MsuPackageBuilder InstallCondition(string condition)
    {
        _installCondition = condition;
        return this;
    }

    public MsuPackageBuilder InstallCondition(Condition condition)
    {
        return InstallCondition(condition.ToString());
    }

    public MsuPackageBuilder Container(string containerId)
    {
        _chainOptions.SetContainer(containerId);
        return this;
    }

    public MsuPackageBuilder Container(ContainerRef containerRef)
    {
        _chainOptions.SetContainer(containerRef);
        return this;
    }

    public MsuPackageBuilder RemotePayload(string url, string sha256, long size)
    {
        _chainOptions.SetRemotePayload(url, sha256, size);
        return this;
    }

    public MsuPackageBuilder ExitCode(int code, ExitCodeBehavior behavior)
    {
        _chainOptions.SetExitCode(code, behavior);
        return this;
    }

    public MsuPackageBuilder DetectionMode(DetectionMode mode)
    {
        _chainOptions.SetDetectionMode(mode);
        return this;
    }

    public MsuPackageBuilder SearchCondition(Action<SearchConditionBuilder> configure)
    {
        _chainOptions.AddSearchCondition(configure);
        return this;
    }

    public MsuPackageBuilder Permanent(bool permanent = true)
    {
        _chainOptions.SetPermanent(permanent);
        return this;
    }

    internal BundlePackageModel Build()
    {
        return new BundlePackageModel
        {
            Id = _id,
            Type = BundlePackageType.MsuPackage,
            DisplayName = _displayName,
            Vital = _vital,
            SourcePath = _sourcePath,
            KbArticle = _kbArticle,
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
