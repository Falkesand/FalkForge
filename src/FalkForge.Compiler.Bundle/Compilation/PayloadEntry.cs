namespace FalkForge.Compiler.Bundle.Compilation;

public sealed class PayloadEntry
{
    public required string PackageId { get; init; }
    public required string SourcePath { get; init; }
    public required long OriginalSize { get; init; }
    public required string Sha256Hash { get; init; }
    public string? ContainerId { get; init; }

    /// <summary>
    /// When true, this payload belongs to a pre-UI prerequisite.
    /// The <see cref="FalkForge.Engine.Protocol.Bundle.TocEntry.IsPreUI"/> flag is set accordingly,
    /// and the engine extracts it into <c>&lt;cacheDir&gt;/preui/</c>.
    /// </summary>
    public bool IsPreUI { get; init; }
}