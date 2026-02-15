namespace FalkInstaller.Compiler.Bundle.Builders;

public sealed class BundlePackageBuilder
{
    private readonly BundlePackageType _type;
    private readonly string _sourcePath;
    private string _id;
    private string _displayName;
    private string? _version;
    private bool _vital = true;
    private string? _installCondition;
    private readonly Dictionary<string, string> _properties = new();
    private readonly Dictionary<int, ExitCodeBehavior> _exitCodes = new();
    private RemotePayloadModel? _remotePayload;
    private string? _containerId;

    internal BundlePackageBuilder(BundlePackageType type, string sourcePath)
    {
        _type = type;
        _sourcePath = sourcePath;
        _id = Path.GetFileNameWithoutExtension(sourcePath);
        _displayName = _id;
    }

    public BundlePackageBuilder Id(string id) { _id = id; return this; }
    public BundlePackageBuilder DisplayName(string name) { _displayName = name; return this; }
    public BundlePackageBuilder Version(string version) { _version = version; return this; }
    public BundlePackageBuilder Vital(bool vital) { _vital = vital; return this; }
    public BundlePackageBuilder InstallCondition(string condition) { _installCondition = condition; return this; }
    public BundlePackageBuilder ExitCode(int code, ExitCodeBehavior behavior) { _exitCodes[code] = behavior; return this; }
    public BundlePackageBuilder Property(string key, string value) { _properties[key] = value; return this; }
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
    public BundlePackageBuilder Container(string containerId) { _containerId = containerId; return this; }

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
            ContainerId = _containerId
        };
    }
}
