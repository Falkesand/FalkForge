namespace FalkForge.Extensions.Iis.Models;

public sealed class CertificateModel
{
    public required string Id { get; init; }
    public CertificateStoreName StoreName { get; init; } = CertificateStoreName.My;
    public CertificateStoreLocation StoreLocation { get; init; } = CertificateStoreLocation.LocalMachine;
    public CertificateFindType FindType { get; init; } = CertificateFindType.FindByThumbprint;
    public required string FindValue { get; init; }
    public bool Exportable { get; init; }
}
