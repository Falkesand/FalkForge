namespace FalkForge.Compiler.Bundle.Builders;

using FalkForge.Engine.Protocol.Manifest;

public sealed class MspPackageBuilder
{
    private readonly ChainPackageOptions _chainOptions = new();
    private readonly string _sourcePath;
    private string _displayName;
    private string _id;
    private string? _installCondition;
    private string? _patchCode;
    private string? _targetProductCode;
    private bool _vital = true;
    private string? _slipstreamTargetId;

    internal MspPackageBuilder(string sourcePath)
    {
        _sourcePath = sourcePath;
        _id = Path.GetFileNameWithoutExtension(sourcePath);
        _displayName = _id;
    }

    public MspPackageBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public MspPackageBuilder DisplayName(string name)
    {
        _displayName = name;
        return this;
    }

    public MspPackageBuilder Vital(bool vital)
    {
        _vital = vital;
        return this;
    }

    public MspPackageBuilder PatchCode(string patchCode)
    {
        _patchCode = patchCode;
        return this;
    }

    public MspPackageBuilder TargetProductCode(string targetProductCode)
    {
        _targetProductCode = targetProductCode;
        return this;
    }

    public MspPackageBuilder InstallCondition(string condition)
    {
        _installCondition = condition;
        return this;
    }

    public MspPackageBuilder InstallCondition(Condition condition)
    {
        return InstallCondition(condition.ToString());
    }

    public MspPackageBuilder SlipstreamTarget(string msiPackageId) { _slipstreamTargetId = msiPackageId; return this; }

    public MspPackageBuilder Container(string containerId)
    {
        _chainOptions.SetContainer(containerId);
        return this;
    }

    public MspPackageBuilder Container(ContainerRef containerRef)
    {
        _chainOptions.SetContainer(containerRef);
        return this;
    }

    public MspPackageBuilder RemotePayload(string url, string sha256, long size)
    {
        _chainOptions.SetRemotePayload(url, sha256, size);
        return this;
    }

    public MspPackageBuilder ExitCode(int code, ExitCodeBehavior behavior)
    {
        _chainOptions.SetExitCode(code, behavior);
        return this;
    }

    public MspPackageBuilder DetectionMode(DetectionMode mode)
    {
        _chainOptions.SetDetectionMode(mode);
        return this;
    }

    public MspPackageBuilder SearchCondition(Action<SearchConditionBuilder> configure)
    {
        _chainOptions.AddSearchCondition(configure);
        return this;
    }

    public MspPackageBuilder Permanent(bool permanent = true)
    {
        _chainOptions.SetPermanent(permanent);
        return this;
    }

    internal BundlePackageModel Build()
    {
        return new BundlePackageModel
        {
            Id = _id,
            Type = BundlePackageType.MspPackage,
            DisplayName = _displayName,
            Vital = _vital,
            SourcePath = _sourcePath,
            PatchCode = _patchCode,
            TargetProductCode = _targetProductCode,
            InstallCondition = _installCondition,
            SlipstreamTargetId = _slipstreamTargetId,
            ContainerId = _chainOptions.ContainerId,
            RemotePayload = _chainOptions.RemotePayload,
            ExitCodes = new Dictionary<int, ExitCodeBehavior>(_chainOptions.ExitCodes),
            DetectionMode = _chainOptions.DetectionMode,
            SearchConditions = _chainOptions.SearchConditions.ToArray(),
            Permanent = _chainOptions.Permanent
        };
    }
}
