namespace FalkForge.Models;

public sealed class SigningOptions
{
    /// <summary>Path to PFX certificate file.</summary>
    public string? CertificatePath { get; init; }

    /// <summary>SHA1 thumbprint of certificate in the certificate store.</summary>
    public string? CertificateThumbprint { get; init; }

    /// <summary>Certificate store name (default: My).</summary>
    public string StoreName { get; init; } = "My";

    /// <summary>RFC 3161 timestamp server URL.</summary>
    public string? TimestampUrl { get; init; }

    /// <summary>Digest algorithm (default: sha256).</summary>
    public string DigestAlgorithm { get; init; } = "sha256";

    /// <summary>Description shown in UAC prompt.</summary>
    public string? Description { get; init; }

    /// <summary>URL shown in UAC prompt.</summary>
    public string? DescriptionUrl { get; init; }
}