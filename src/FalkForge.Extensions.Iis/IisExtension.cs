using System.Runtime.Versioning;
using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Builders;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

[SupportedOSPlatform("windows")]
public sealed class IisExtension : IFalkForgeExtension
{
    private readonly List<WebSiteModel> _webSites = [];
    private readonly List<AppPoolModel> _appPools = [];
    private readonly List<CertificateModel> _certificates = [];

    public string Name => "Iis";

    public IReadOnlyList<WebSiteModel> WebSites => _webSites;
    public IReadOnlyList<AppPoolModel> AppPools => _appPools;
    public IReadOnlyList<CertificateModel> Certificates => _certificates;

    public IisExtension AddWebSite(Action<WebSiteBuilder> configure)
    {
        var builder = new WebSiteBuilder();
        configure(builder);
        _webSites.Add(builder.Build());
        return this;
    }

    public IisExtension AddAppPool(Action<AppPoolBuilder> configure)
    {
        var builder = new AppPoolBuilder();
        configure(builder);
        _appPools.Add(builder.Build());
        return this;
    }

    public IisExtension AddCertificate(Action<CertificateBuilder> configure)
    {
        var builder = new CertificateBuilder();
        configure(builder);
        _certificates.Add(builder.Build());
        return this;
    }

    public Result<Unit> Validate() =>
        IisValidator.ValidateAll(_webSites, _appPools, _certificates);

    public void Register(IExtensionRegistry registry)
    {
        // IIS extension is model-only at compile time.
        // The actual IIS management happens via custom actions at install time
        // using Microsoft.Web.Administration.
    }
}
