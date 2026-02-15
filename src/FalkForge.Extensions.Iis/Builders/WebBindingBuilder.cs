using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis.Builders;

public sealed class WebBindingBuilder
{
    private string _protocol = "http";
    private int _port;
    private string? _hostHeader;
    private string _ipAddress = "*";
    private string? _certificateRef;

    public WebBindingBuilder Protocol(string protocol)
    {
        _protocol = protocol;
        return this;
    }

    public WebBindingBuilder Port(int port)
    {
        _port = port;
        return this;
    }

    public WebBindingBuilder HostHeader(string hostHeader)
    {
        _hostHeader = hostHeader;
        return this;
    }

    public WebBindingBuilder IpAddress(string ipAddress)
    {
        _ipAddress = ipAddress;
        return this;
    }

    public WebBindingBuilder Certificate(string certificateRef)
    {
        _protocol = "https";
        _certificateRef = certificateRef;
        return this;
    }

    internal WebBindingModel Build() => new()
    {
        Protocol = _protocol,
        Port = _port,
        HostHeader = _hostHeader,
        IpAddress = _ipAddress,
        CertificateRef = _certificateRef
    };
}
