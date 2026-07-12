namespace FalkForge.Compiler.Bundle;

public sealed record RemotePayloadModel
{
    public required string DownloadUrl { get; init; }
    public required string Sha256Hash { get; init; }
    public required long Size { get; init; }

    /// <summary>
    /// Optional publisher public-key pin: the SHA-256 hash (hex, 64 chars) of the signer
    /// certificate's SubjectPublicKeyInfo. Flows through the manifest to the engine's
    /// payload-verification components (package cache / offline layout), which — after the SHA-256
    /// check — require the payload to carry a valid Authenticode signature whose signer public key
    /// hashes to this value, and fail closed otherwise. See
    /// <see cref="FalkForge.Engine.Protocol.Manifest.PackageInfo.RemotePayloadCertificatePublicKey"/>
    /// for the enforcement-layer / pipeline-wiring note. Null means no publisher pin (a valid SHA-256
    /// is the only integrity guarantee).
    /// </summary>
    public string? CertificatePublicKey { get; init; }
}