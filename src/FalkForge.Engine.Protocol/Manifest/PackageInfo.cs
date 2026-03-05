namespace FalkForge.Engine.Protocol.Manifest;

public sealed class PackageInfo
{
    public required string Id { get; init; }
    public required PackageType Type { get; init; }
    public required string DisplayName { get; init; }
    public string? Version { get; init; }
    public bool Vital { get; init; } = true;
    public required string SourcePath { get; init; }
    public required string Sha256Hash { get; init; }
    public Dictionary<string, string> Properties { get; init; } = new();
    public string? InstallCondition { get; init; }
    public IReadOnlyDictionary<int, ExitCodeBehavior>? ExitCodes { get; init; }
    public string? KbArticle { get; init; }
    public string? PatchCode { get; init; }
    public string? TargetProductCode { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ContainerId { get; init; }
    public DetectionMode DetectionMode { get; init; } = DetectionMode.Default;
    public IReadOnlyList<SearchCondition> SearchConditions { get; init; } = [];
    public string? AuthenticodeThumbprint { get; init; }
    public bool IsPrerequisite { get; init; }
}
