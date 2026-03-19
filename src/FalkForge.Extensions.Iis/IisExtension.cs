using System.Runtime.Versioning;
using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Builders;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

[SupportedOSPlatform("windows")]
public sealed class IisExtension : IFalkForgeExtension, IDryRunContributor
{
    private readonly List<AppPoolModel> _appPools = [];
    private readonly List<CertificateModel> _certificates = [];
    private readonly List<WebSiteModel> _webSites = [];

    public IReadOnlyList<WebSiteModel> WebSites => _webSites;
    public IReadOnlyList<AppPoolModel> AppPools => _appPools;
    public IReadOnlyList<CertificateModel> Certificates => _certificates;

    public string Name => "Iis";

    public void Register(IExtensionRegistry registry)
    {
        // IIS extension is model-only at compile time.
        // The actual IIS management happens via custom actions at install time
        // using Microsoft.Web.Administration.
    }

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

    public AppPoolRef DefineAppPool(Action<AppPoolBuilder> configure)
    {
        var builder = new AppPoolBuilder();
        configure(builder);
        var model = builder.Build();
        _appPools.Add(model);
        return new AppPoolRef(model.Id);
    }

    public IisExtension AddCertificate(Action<CertificateBuilder> configure)
    {
        var builder = new CertificateBuilder();
        configure(builder);
        _certificates.Add(builder.Build());
        return this;
    }

    public CertificateRef DefineCertificate(Action<CertificateBuilder> configure)
    {
        var builder = new CertificateBuilder();
        configure(builder);
        var model = builder.Build();
        _certificates.Add(model);
        return new CertificateRef(model.Id);
    }

    public Result<Unit> Validate()
    {
        return IisValidator.ValidateAll(_webSites, _appPools, _certificates);
    }

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install =>
            [
                new DryRunAction { Kind = DryRunActionKind.Network, Description = "Would create IIS application pool(s)" },
                new DryRunAction { Kind = DryRunActionKind.Network, Description = "Would create IIS web site(s) with binding(s)" }
            ],
            DryRunIntent.Uninstall =>
            [
                new DryRunAction { Kind = DryRunActionKind.Network, Description = "Would remove IIS application pool(s)" },
                new DryRunAction { Kind = DryRunActionKind.Network, Description = "Would remove IIS web site(s)" }
            ],
            _ => []
        };
}