namespace FalkForge.Extensions.Iis.Models;

public sealed class WebBindingModel
{
    public string Protocol { get; init; } = "http";
    public int Port { get; init; }
    public string? HostHeader { get; init; }
    public string IpAddress { get; init; } = "*";
    public string? CertificateRef { get; init; }
}