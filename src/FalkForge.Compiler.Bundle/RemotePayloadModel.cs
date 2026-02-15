namespace FalkForge.Compiler.Bundle;

public sealed record RemotePayloadModel
{
    public required string DownloadUrl { get; init; }
    public required string Sha256Hash { get; init; }
    public required long Size { get; init; }
    public string? CertificatePublicKey { get; init; }
}
