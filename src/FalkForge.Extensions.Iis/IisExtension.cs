using System.Collections.Immutable;
using System.Runtime.Versioning;
using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Builders;
using FalkForge.Extensions.Iis.Models;
using FalkForge.Validation;

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
        ArgumentNullException.ThrowIfNull(registry);

        // Emit the configured application pools and web sites as inspectable custom MSI tables
        // (IIsAppPool / IIsWebSite), plus a placeholder CustomAction that records the deferred
        // "configure IIS" step. The placeholder action is intentionally never scheduled into an
        // install sequence, so it does not execute.
        //
        // What is NOT yet implemented: install-time IIS management via
        // Microsoft.Web.Administration (creating the pools/sites/bindings for real), certificate
        // emission, and a dedicated multi-binding table. Those are follow-ups; the tables emitted
        // here make the configuration present and inspectable in the compiled MSI so the extension
        // is no longer a silent no-op.
        registry.RegisterTableContributor(new IisAppPoolTableContributor(() => _appPools));
        registry.RegisterTableContributor(new IisWebSiteTableContributor(() => _webSites));
        registry.RegisterTableContributor(
            new IisConfigCustomActionContributor(() => _appPools.Count > 0 || _webSites.Count > 0));
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

    /// <inheritdoc/>
    public ImmutableArray<ValidationRule> GetValidationRules()
        => IisRules.Build(() => _webSites, () => _appPools, () => _certificates);

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