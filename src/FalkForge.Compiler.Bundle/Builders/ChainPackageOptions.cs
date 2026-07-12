namespace FalkForge.Compiler.Bundle.Builders;

using FalkForge.Engine.Protocol.Manifest;

/// <summary>
///     Shared mutable state for the six chain-package knobs (Container, RemotePayload, ExitCode,
///     DetectionMode, SearchCondition, Permanent) that <see cref="BundlePackageModel" /> honors
///     identically regardless of package type. <see cref="MsuPackageBuilder" />,
///     <see cref="MspPackageBuilder" />, and <see cref="NestedBundlePackageBuilder" /> each hold one
///     instance and forward their same-named fluent methods to it, so the field declarations and
///     method bodies live in exactly one place instead of being copy-pasted three times over.
///     <see cref="BundlePackageBuilder" /> keeps its own pre-existing implementation of these same
///     knobs unchanged — it is not part of this extraction to keep the change surgical.
/// </summary>
internal sealed class ChainPackageOptions
{
    private readonly Dictionary<int, ExitCodeBehavior> _exitCodes = new();
    private readonly List<SearchCondition> _searchConditions = new();

    public string? ContainerId { get; private set; }
    public RemotePayloadModel? RemotePayload { get; private set; }
    public DetectionMode DetectionMode { get; private set; } = Engine.Protocol.Manifest.DetectionMode.Default;
    public bool Permanent { get; private set; }
    public IReadOnlyDictionary<int, ExitCodeBehavior> ExitCodes => _exitCodes;
    public IReadOnlyList<SearchCondition> SearchConditions => _searchConditions;

    public void SetContainer(string containerId)
    {
        ContainerId = containerId;
    }

    public void SetContainer(ContainerRef containerRef)
    {
        SetContainer(containerRef.Id);
    }

    public void SetRemotePayload(string url, string sha256, long size)
    {
        RemotePayload = new RemotePayloadModel
        {
            DownloadUrl = url,
            Sha256Hash = sha256,
            Size = size
        };
    }

    public void SetExitCode(int code, ExitCodeBehavior behavior)
    {
        _exitCodes[code] = behavior;
    }

    public void SetDetectionMode(DetectionMode mode)
    {
        DetectionMode = mode;
    }

    public void AddSearchCondition(Action<SearchConditionBuilder> configure)
    {
        var builder = new SearchConditionBuilder();
        configure(builder);
        _searchConditions.Add(builder.Build());
    }

    public void SetPermanent(bool permanent)
    {
        Permanent = permanent;
    }
}
