namespace FalkForge.Engine.Protocol.Bundle;

public sealed class BundleContent
{
    public required TocEntry[] TocEntries { get; init; }
    public required string BundlePath { get; init; }
}
