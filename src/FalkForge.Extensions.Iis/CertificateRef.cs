namespace FalkForge.Extensions.Iis;

public sealed record CertificateRef
{
    public CertificateRef(string Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
    }

    public string Id { get; }
}