using System.Security.Cryptography;
using System.Text;
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Builders;

public sealed class SniSslBindingBuilder(string hostname, int port)
{
    private string _thumbprint    = "";
    private Guid   _appId;
    private string _certStoreName = "MY";

    public SniSslBindingBuilder Thumbprint(string thumbprint)   { _thumbprint = thumbprint;   return this; }
    public SniSslBindingBuilder AppId(Guid appId)               { _appId = appId;             return this; }
    public SniSslBindingBuilder CertStoreName(string storeName) { _certStoreName = storeName; return this; }

    internal SniSslBindingModel Build()
    {
        var appId = _appId == Guid.Empty ? DeriveAppId(hostname, port) : _appId;
        return new SniSslBindingModel
        {
            Hostname              = hostname,
            Port                  = port,
            CertificateThumbprint = _thumbprint,
            AppId                 = appId,
            CertStoreName         = _certStoreName
        };
    }

    private static Guid DeriveAppId(string host, int p)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{host}:{p}"));
        return new Guid(bytes[..16]);
    }
}
