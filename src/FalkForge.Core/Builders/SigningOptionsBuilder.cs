namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class SigningOptionsBuilder
{
    public string? CertificatePath { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string StoreName { get; set; } = "My";
    public string? TimestampUrl { get; set; }
    public string DigestAlgorithm { get; set; } = "sha256";
    public string? AdditionalArguments { get; set; }
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
        DigestAlgorithm = algorithm;
        return this;
    }

    public SigningOptionsBuilder WithDescription(string description, string? url = null)
    {
        Description = description;
        DescriptionUrl = url;
        return this;
    }

    internal SigningOptions Build() => new()
    {
        CertificatePath = CertificatePath,
        CertificateThumbprint = CertificateThumbprint,
        StoreName = StoreName,
        TimestampUrl = TimestampUrl,
        DigestAlgorithm = DigestAlgorithm,
        AdditionalArguments = AdditionalArguments,
        Description = Description,
        DescriptionUrl = DescriptionUrl
    };
}
