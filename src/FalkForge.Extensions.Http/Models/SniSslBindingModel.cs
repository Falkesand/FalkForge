namespace FalkForge.Extensions.Http.Models;

public sealed record SniSslBindingModel(
    string Hostname,
    int Port,
    string CertificateThumbprint,
    Guid AppId,
    string CertStoreName = "MY");
