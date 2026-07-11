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
        // (IIsAppPool / IIsWebSite) for decompile/inspection record...
        registry.RegisterTableContributor(new IisAppPoolTableContributor(() => _appPools));
        registry.RegisterTableContributor(new IisWebSiteTableContributor(() => _webSites));
        // ...and make those tables LIVE: schedule deferred, elevated custom actions that create the
        // application pools and web sites (with ALL their bindings) at install via
        // Microsoft.Web.Administration, and remove them on uninstall (with rollback on a failed
        // install). This replaces the former inert placeholder CustomAction.
        // Each SpecificUser pool's create step declares its ExecutionStep.HiddenProperties; the compiler
        // aggregates those across all extensions into a single MsiHiddenProperties row that scrubs the
        // app-pool password (carried through the CustomActionData channel) from verbose MSI logs.
        registry.RegisterExecutionContributor(new IisExecutionContributor(() => _appPools, () => _webSites));

        // Deferred to a follow-up (surfaced as fail-loud IIS013/IIS014 warnings so they are never a silent
        // no-op): certificate emission + SSL-certificate binding, and sub-application/virtual-directory
        // creation. The HTTPS binding entry itself is still written into the site configuration.
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