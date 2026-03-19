namespace FalkForge.Extensions.Http.Models;

public sealed class SniSslBindingModel
{
    public required string Hostname { get; init; }
    public required int Port { get; init; }
    public required string CertificateThumbprint { get; init; }
    public required Guid AppId { get; init; }
    public string CertStoreName { get; init; } = "MY";
}
