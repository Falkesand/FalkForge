namespace FalkForge.Extensions.Iis;

public sealed record CertificateRef
{
    public string Id { get; }

    public CertificateRef(string Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
    }
}
