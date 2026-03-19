namespace FalkForge.Engine.Protocol.Bundle;

public sealed class BundleContent
{
    public required TocEntry[] TocEntries { get; init; }
    public required string BundlePath { get; init; }

    /// <summary>
    /// The raw manifest JSON bytes embedded in the bundle.
    /// Null when the bundle was created without an embedded manifest.
    /// </summary>
    public byte[]? ManifestJsonBytes { get; init; }
}
