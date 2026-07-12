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
    private bool _permanent;
    private bool _enableFeatureSelection;

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

    /// <param name="url">HTTPS download URL of the remote payload.</param>
    /// <param name="sha256">Expected SHA-256 (hex) of the downloaded bytes.</param>
    /// <param name="size">Expected size in bytes.</param>
    /// <param name="certificatePublicKey">
    /// Optional publisher pin: the SHA-256 hash (hex, 64 chars) of the signer certificate's
    /// SubjectPublicKeyInfo. When set, the engine additionally requires the downloaded payload to be
    /// validly Authenticode-signed by this publisher key (in addition to the SHA-256 check) and
    /// aborts the install on an unsigned/invalid/wrong-signer payload. Unlike a certificate
    /// thumbprint, this pin survives certificate reissuance with the same key pair — the right fit
    /// for a remote payload whose bytes may update but whose publisher is fixed.
    /// </param>
    public BundlePackageBuilder RemotePayload(string url, string sha256, long size, string? certificatePublicKey = null)
    {
        _remotePayload = new RemotePayloadModel
        {
            DownloadUrl = url,
            Sha256Hash = sha256,
            Size = size,
            CertificatePublicKey = certificatePublicKey
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
    public BundlePackageBuilder Permanent(bool permanent = true) { _permanent = permanent; return this; }
    public BundlePackageBuilder EnableFeatureSelection(bool enable = true) { _enableFeatureSelection = enable; return this; }

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
            IsPrerequisite = _isPrerequisite,
            Permanent = _permanent,
            EnableFeatureSelection = _enableFeatureSelection
        };
    }
}