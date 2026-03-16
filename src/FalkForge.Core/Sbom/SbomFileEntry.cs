namespace FalkForge.Sbom;

public sealed record SbomFileEntry(string FileName, string Sha256Hash, long FileSize, string? Version);
