namespace FalkForge.Compiler.Bundle.Builders;

using FalkForge.Engine.Protocol.Manifest;

public sealed class MsuPackageBuilder
{
    private readonly Dictionary<int, ExitCodeBehavior> _exitCodes = new();
    private readonly List<SearchCondition> _searchConditions = new();
    private readonly string _sourcePath;
    private string? _containerId;
    private DetectionMode _detectionMode = Engine.Protocol.Manifest.DetectionMode.Default;
    private string _displayName;
    private string _id;
    private string? _installCondition;
    private string? _kbArticle;
    private bool _permanent;
    private RemotePayloadModel? _remotePayload;
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
        _containerId = containerId;
        return this;
    }

    public MsuPackageBuilder Container(ContainerRef containerRef)
    {
        return Container(containerRef.Id);
    }

    public MsuPackageBuilder RemotePayload(string url, string sha256, long size)
    {
        _remotePayload = new RemotePayloadModel
        {
            DownloadUrl = url,
            Sha256Hash = sha256,
            Size = size
        };
        return this;
    }

    public MsuPackageBuilder ExitCode(int code, ExitCodeBehavior behavior)
    {
        _exitCodes[code] = behavior;
        return this;
    }

    public MsuPackageBuilder DetectionMode(DetectionMode mode)
    {
        _detectionMode = mode;
        return this;
    }

    public MsuPackageBuilder SearchCondition(Action<SearchConditionBuilder> configure)
    {
        var builder = new SearchConditionBuilder();
        configure(builder);
        _searchConditions.Add(builder.Build());
        return this;
    }

    public MsuPackageBuilder Permanent(bool permanent = true)
    {
        _permanent = permanent;
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
            ContainerId = _containerId,
            RemotePayload = _remotePayload,
            ExitCodes = new Dictionary<int, ExitCodeBehavior>(_exitCodes),
            DetectionMode = _detectionMode,
            SearchConditions = _searchConditions.ToArray(),
            Permanent = _permanent
        };
    }
}