using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class SigningOptionsBuilder
{
    private static readonly string[] AllowedDigestAlgorithms = ["sha256", "sha384", "sha512"];
    public string? CertificatePath { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string StoreName { get; set; } = "My";
    public string? TimestampUrl { get; set; }
    public string DigestAlgorithm { get; set; } = "sha256";
    public string? Description { get; set; }
    public string? DescriptionUrl { get; set; }

    public SigningOptionsBuilder Certificate(string pfxPath)
    {
        CertificatePath = pfxPath;
        return this;
    }

    public SigningOptionsBuilder Thumbprint(string thumbprint)
    {
        CertificateThumbprint = thumbprint;
        return this;
    }

    public SigningOptionsBuilder Store(string storeName)
    {
        StoreName = storeName;
        return this;
    }

    public SigningOptionsBuilder Timestamp(string url)
    {
        TimestampUrl = url;
        return this;
    }

    public SigningOptionsBuilder Algorithm(string algorithm)
    {
        ArgumentException.ThrowIfNullOrEmpty(algorithm);
        if (Array.IndexOf(AllowedDigestAlgorithms, algorithm) < 0)
            throw new ArgumentException(
                $"DigestAlgorithm '{algorithm}' is not allowed. Must be one of: sha256, sha384, sha512.",
                nameof(algorithm));
        DigestAlgorithm = algorithm;
        return this;
    }

    public SigningOptionsBuilder WithDescription(string description, string? url = null)
    {
        Description = description;
        DescriptionUrl = url;
        return this;
    }

    internal SigningOptions Build()
    {
        return new SigningOptions
        {
            CertificatePath = CertificatePath,
            CertificateThumbprint = CertificateThumbprint,
            StoreName = StoreName,
            TimestampUrl = TimestampUrl,
            DigestAlgorithm = DigestAlgorithm,
            Description = Description,
            DescriptionUrl = DescriptionUrl
        };
    }
}