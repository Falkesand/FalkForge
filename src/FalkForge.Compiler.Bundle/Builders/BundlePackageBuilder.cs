namespace FalkForge.Compiler.Bundle.Builders;

using FalkForge.Engine.Protocol.Manifest;

public sealed class BundlePackageBuilder
{
    private readonly Dictionary<int, ExitCodeBehavior> _exitCodes = new();
    private readonly Dictionary<string, string> _properties = new();
    private readonly string _sourcePath;
    private readonly BundlePackageType _type;
    private string? _containerId;
    private string _displayName;
    private string _id;
    private string? _installCondition;
    private RemotePayloadModel? _remotePayload;
    private string? _version;
    private bool _vital = true;
    private DetectionMode _detectionMode = Engine.Protocol.Manifest.DetectionMode.Default;
    private readonly List<SearchCondition> _searchConditions = new();
    private string? _authenticodeThumbprint;
    private bool _isPrerequisite;

    internal BundlePackageBuilder(BundlePackageType type, string sourcePath)
    {
        _type = type;
        _sourcePath = sourcePath;
        _id = Path.GetFileNameWithoutExtension(sourcePath);
        _displayName = _id;
    }

    public BundlePackageBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public BundlePackageBuilder DisplayName(string name)
    {
        _displayName = name;
        return this;
    }

    public BundlePackageBuilder Version(string version)
    {
        _version = version;
        return this;
    }

    public BundlePackageBuilder Vital(bool vital)
    {
        _vital = vital;
        return this;
    }

    public BundlePackageBuilder InstallCondition(string condition)
    {
        _installCondition = condition;
        return this;
    }

    public BundlePackageBuilder InstallCondition(Condition condition)
    {
        return InstallCondition(condition.ToString());
    }

    public BundlePackageBuilder ExitCode(int code, ExitCodeBehavior behavior)
    {
        _exitCodes[code] = behavior;
        return this;
    }

    public BundlePackageBuilder Property(string key, string value)
    {
        _properties[key] = value;
        return this;
    }

    public BundlePackageBuilder RemotePayload(string url, string sha256, long size)
    {
        _remotePayload = new RemotePayloadModel
        {
            DownloadUrl = url,
            Sha256Hash = sha256,
            Size = size
        };
        return this;
    }

    public BundlePackageBuilder Container(string containerId)
    {
        _containerId = containerId;
        return this;
    }

    public BundlePackageBuilder Container(ContainerRef containerRef)
    {
        return Container(containerRef.Id);
    }

    public BundlePackageBuilder DetectionMode(DetectionMode mode) { _detectionMode = mode; return this; }
    public BundlePackageBuilder SearchCondition(Action<SearchConditionBuilder> configure)
    {
        var builder = new SearchConditionBuilder();
        configure(builder);
        _searchConditions.Add(builder.Build());
        return this;
    }
    public BundlePackageBuilder AuthenticodeThumbprint(string thumbprint) { _authenticodeThumbprint = thumbprint; return this; }
    public BundlePackageBuilder Prerequisite(bool isPrerequisite = true) { _isPrerequisite = isPrerequisite; return this; }

    internal BundlePackageModel Build()
    {
        return new BundlePackageModel
        {
            Id = _id,
            Type = _type,
            DisplayName = _displayName,
            Version = _version,
            Vital = _vital,
            SourcePath = _sourcePath,
            Properties = new Dictionary<string, string>(_properties),
            InstallCondition = _installCondition,
            ExitCodes = new Dictionary<int, ExitCodeBehavior>(_exitCodes),
            RemotePayload = _remotePayload,
            ContainerId = _containerId,
            DetectionMode = _detectionMode,
            SearchConditions = _searchConditions.ToArray(),
            AuthenticodeThumbprint = _authenticodeThumbprint,
            IsPrerequisite = _isPrerequisite
        };
    }
}