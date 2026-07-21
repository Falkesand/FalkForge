using System.Text;
using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Models;
using Xunit;

namespace FalkForge.Extensions.Iis.Tests;

/// <summary>
/// Unit-level proof of the IIS execution command generation — injection safety of author/environment
/// values, the secure credential channel, the physical-path channel, and the IIS-prerequisite fail-loud
/// guard — independent of an MSI compile. Mirrors <c>SqlCommandFactoryTests</c>.
/// </summary>
public sealed class IisCommandFactoryTests
{
    [Fact]
    public void MaliciousSiteName_IsSingleQuotedLiteral_CannotBreakOut()
    {
        const string evil = "Site'; Remove-Item C:\\ -Recurse #";
        var sites = new[] { Site("S", evil, "[INSTALLDIR]web") };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, []), "IisSite_S"));

        // The name must appear only as a single-quoted PowerShell literal with its quote doubled — never as
        // raw, breakable text. So the raw "'; Remove-Item" sequence must NOT be present verbatim.
        Assert.Contains("Site''; Remove-Item C:\\ -Recurse #'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("['Site'; Remove-Item", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MaliciousHostHeader_IsSingleQuotedLiteral()
    {
        var binding = new WebBindingModel { Port = 80, HostHeader = "h'; calc #" };
        var sites = new[] { new WebSiteModel { Id = "S", Description = "Site", Directory = "[INSTALLDIR]", Bindings = [binding] } };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, []), "IisSite_S"));

        Assert.Contains("''; calc #", script, StringComparison.Ordinal); // doubled quote → inert
    }

    [Fact]
    public void PhysicalPath_RidesCustomActionDataChannel_NotBakedIntoScript()
    {
        var sites = new[] { Site("S", "Site", "[INSTALLDIR]web") };
        ExecutionStep step = Single(IisCommandFactory.BuildSteps([], sites, []), "IisSite_S");

        // The path is passed via the CustomActionData channel (so [INSTALLDIR] resolves at schedule time),
        // and the script consumes it as a runtime $__arg — never concatenated into the script body.
        Assert.Equal("[INSTALLDIR]web", step.CustomActionData);
        string script = DecodeInstall(step);
        Assert.Contains("$__arg", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[INSTALLDIR]web", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SpecificUserSecure_UsesPropertyToken_AndSetsPasswordFromArg_NeverLiteral()
    {
        var pools = new[] { SpecificUserPool("P", "Pool", "domain\\svc", passwordProperty: "IISPWD", password: null) };
        ExecutionStep create = Single(IisCommandFactory.BuildSteps(pools, [], []), "IisPool_P");

        Assert.Equal("[IISPWD]", create.CustomActionData);
        string script = DecodeInstall(create);
        Assert.Contains("$__pool.ProcessModel.Password = $__arg", script, StringComparison.Ordinal);

        // The create step declares the scrub list the compiler aggregates into MsiHiddenProperties.
        Assert.Contains("IISPWD", create.HiddenProperties);
        Assert.Contains("IisPool_P", create.HiddenProperties);
    }

    [Fact]
    public void SpecificUserLiteral_EmbedsEscapedPassword_InCustomActionData()
    {
        var pools = new[] { SpecificUserPool("P", "Pool", "domain\\svc", passwordProperty: null, password: "Pl4in") };
        ExecutionStep create = Single(IisCommandFactory.BuildSteps(pools, [], []), "IisPool_P");

        Assert.Equal("Pl4in", create.CustomActionData); // literal (MSI-escaped) — the discouraged path
        // Both the source-less literal and the deferred action property are still hidden from the log.
        Assert.Contains("IisPool_P", create.HiddenProperties);
    }

    [Fact]
    public void NonSpecificUserPool_HasNoCredentialChannel_AndNoHiddenProperties()
    {
        var pools = new[] { new AppPoolModel { Id = "P", Name = "Pool", IdentityType = AppPoolIdentityType.NetworkService } };
        ExecutionStep create = Single(IisCommandFactory.BuildSteps(pools, [], []), "IisPool_P");

        Assert.Null(create.CustomActionData);
        Assert.Empty(create.HiddenProperties);
    }

    [Fact]
    public void PoolsCreatedFirst_SitesNext_PoolRemovedLast()
    {
        var pools = new[] { new AppPoolModel { Id = "P", Name = "Pool" } };
        var sites = new[] { Site("S", "Site", "[INSTALLDIR]", "P") };

        var ids = IisCommandFactory.BuildSteps(pools, sites, []).Select(s => s.Id).ToList();

        Assert.True(ids.IndexOf("IisPool_P") < ids.IndexOf("IisSite_S"));
        Assert.True(ids.IndexOf("IisSite_S") < ids.IndexOf("IisPoolDel_P"));
    }

    [Fact]
    public void EveryInstallScript_GuardsIisPrerequisite_FailLoud()
    {
        var pools = new[] { new AppPoolModel { Id = "P", Name = "Pool" } };
        var sites = new[] { Site("S", "Site", "[INSTALLDIR]", "P") };

        foreach (ExecutionStep step in IisCommandFactory.BuildSteps(pools, sites, []))
        {
            if (step.InstallCondition == "0")
                continue; // gated-off no-op install command for the uninstall-only pool-remove step
            string script = DecodeInstall(step);
            Assert.Contains("W3SVC", script, StringComparison.Ordinal);
            Assert.Contains("throw", script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AllBindings_AppearInSiteScript()
    {
        var bindings = new WebBindingModel[]
        {
            new() { Port = 80 },
            new() { Port = 8080, HostHeader = "a.example" },
            new() { Port = 8081, HostHeader = "b.example" },
        };
        var sites = new[] { new WebSiteModel { Id = "S", Description = "Site", Directory = "[INSTALLDIR]", Bindings = bindings } };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, []), "IisSite_S"));

        Assert.Contains("*:80:", script, StringComparison.Ordinal);
        Assert.Contains("*:8080:a.example", script, StringComparison.Ordinal);
        Assert.Contains("*:8081:b.example", script, StringComparison.Ordinal);
    }

    // ── virtual directories ──────────────────────────────────────────────────

    [Fact]
    public void MaliciousVdirAlias_IsSingleQuotedLiteral_CannotBreakOut()
    {
        const string evil = "/reports'; Remove-Item C:\\ -Recurse #";
        var sites = new[] { SiteWithVdir("S", "Site", vdirId: "V", alias: evil, dir: "[INSTALLDIR]reports") };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, []), "IisVDir_V"));

        Assert.Contains("reports''; Remove-Item C:\\ -Recurse #'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("['/reports'; Remove-Item", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VdirPhysicalPath_RidesCustomActionDataChannel_NotBakedIntoScript()
    {
        var sites = new[] { SiteWithVdir("S", "Site", vdirId: "V", alias: "/reports", dir: "[INSTALLDIR]reports") };
        ExecutionStep step = Single(IisCommandFactory.BuildSteps([], sites, []), "IisVDir_V");

        Assert.Equal("[INSTALLDIR]reports", step.CustomActionData);
        string script = DecodeInstall(step);
        Assert.Contains("$__arg", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[INSTALLDIR]reports", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VdirCreatedAfterSite_InGeneratedStepOrder()
    {
        var sites = new[] { SiteWithVdir("S", "Site", vdirId: "V", alias: "/reports", dir: "[INSTALLDIR]reports") };

        var ids = IisCommandFactory.BuildSteps([], sites, []).Select(s => s.Id).ToList();

        Assert.True(ids.IndexOf("IisSite_S") < ids.IndexOf("IisVDir_V"));
    }

    [Fact]
    public void VdirWithoutWebApplication_TargetsRootApplication()
    {
        var sites = new[] { SiteWithVdir("S", "Site", vdirId: "V", alias: "/reports", dir: "[INSTALLDIR]reports") };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, []), "IisVDir_V"));

        Assert.Contains("$__site.Applications['/']", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VdirWithWebApplication_TargetsNamedApplication()
    {
        var vdir = new WebVirtualDirectoryModel { Id = "V", Alias = "/reports", Directory = "[INSTALLDIR]reports", WebApplication = "/api" };
        var sites = new[] { new WebSiteModel { Id = "S", Description = "Site", Directory = "[INSTALLDIR]", Bindings = [new WebBindingModel { Port = 80 }], VirtualDirectories = [vdir] } };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, []), "IisVDir_V"));

        Assert.Contains("$__site.Applications['/api']", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VdirCreateScript_GuardsIisPrerequisite_FailLoud()
    {
        var sites = new[] { SiteWithVdir("S", "Site", vdirId: "V", alias: "/reports", dir: "[INSTALLDIR]reports") };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, []), "IisVDir_V"));

        Assert.Contains("W3SVC", script, StringComparison.Ordinal);
        Assert.Contains("throw", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VdirCreateScript_ThrowsLoud_WhenParentApplicationMissing()
    {
        var sites = new[] { SiteWithVdir("S", "Site", vdirId: "V", alias: "/reports", dir: "[INSTALLDIR]reports") };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, []), "IisVDir_V"));

        // Fail loud (never a silent no-op) when the parent application does not exist at install time.
        Assert.Contains("$__app = $__site.Applications['/']", script, StringComparison.Ordinal);
        Assert.Contains("if ($null -eq $__app) { throw", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VdirRemoveScript_IsToleratedAndRemovesFromParentApplication()
    {
        var sites = new[] { SiteWithVdir("S", "Site", vdirId: "V", alias: "/reports", dir: "[INSTALLDIR]reports") };
        ExecutionStep step = Single(IisCommandFactory.BuildSteps([], sites, []), "IisVDir_V");

        Assert.NotNull(step.RollbackCommand);
        Assert.NotNull(step.UninstallCommand);

        string removeScript = DecodeEncodedScript(step.RollbackCommand!);
        Assert.Contains("VirtualDirectories.Remove", removeScript, StringComparison.Ordinal);
        // Tolerant: a missing site/application must not fail the rollback/uninstall action.
        Assert.Contains("catch { [Console]::Error.WriteLine($_.Exception.Message); exit 0 }", removeScript, StringComparison.Ordinal);
    }

    // ── web applications (sub-applications) ──────────────────────────────────

    [Fact]
    public void WebApplication_CreateStep_IsSequencedAfterSite_AndBeforeVirtualDirectory()
    {
        var app = new WebApplicationModel { Id = "Api", Alias = "/api", Directory = "[INSTALLDIR]api" };
        var vdir = new WebVirtualDirectoryModel { Id = "Reports", Alias = "/reports", Directory = "[INSTALLDIR]reports", WebApplication = "/api" };
        var sites = new[]
        {
            new WebSiteModel
            {
                Id = "S", Description = "Site", Directory = "[INSTALLDIR]",
                Bindings = [new WebBindingModel { Port = 80 }],
                WebApplications = [app],
                VirtualDirectories = [vdir],
            },
        };

        var ids = IisCommandFactory.BuildSteps([], sites, []).Select(s => s.Id).ToList();

        // The sub-application is genuinely created — and ordered so it exists before a virtual directory that
        // is parented under it.
        Assert.Contains("IisApp_Api", ids);
        Assert.True(ids.IndexOf("IisSite_S") < ids.IndexOf("IisApp_Api"), "application created after its site");
        Assert.True(ids.IndexOf("IisApp_Api") < ids.IndexOf("IisVDir_Reports"), "application created before a vdir under it");
    }

    [Fact]
    public void WebApplicationCreateScript_AddsApplication_AndSetsAppPool()
    {
        var app = new WebApplicationModel { Id = "Api", Alias = "/api", Directory = "[INSTALLDIR]api", AppPool = "P" };
        var pools = new[] { new AppPoolModel { Id = "P", Name = "ApiPool" } };
        var sites = new[] { SiteWithApp("S", "Site", app) };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps(pools, sites, []), "IisApp_Api"));

        Assert.Contains("$__site.Applications.Add('/api', $__arg)", script, StringComparison.Ordinal);
        // The pool reference resolves by Id to the pool's real name.
        Assert.Contains("$__app.ApplicationPoolName = 'ApiPool'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WebApplicationPhysicalPath_RidesCustomActionDataChannel_NotBakedIntoScript()
    {
        var app = new WebApplicationModel { Id = "Api", Alias = "/api", Directory = "[INSTALLDIR]api" };
        ExecutionStep step = Single(IisCommandFactory.BuildSteps([], [SiteWithApp("S", "Site", app)], []), "IisApp_Api");

        Assert.Equal("[INSTALLDIR]api", step.CustomActionData);
        string script = DecodeInstall(step);
        Assert.Contains("$__arg", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[INSTALLDIR]api", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MaliciousWebApplicationAlias_IsSingleQuotedLiteral_CannotBreakOut()
    {
        const string evil = "/api'; Remove-Item C:\\ -Recurse #";
        var app = new WebApplicationModel { Id = "Api", Alias = evil, Directory = "[INSTALLDIR]api" };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], [SiteWithApp("S", "Site", app)], []), "IisApp_Api"));

        Assert.Contains("api''; Remove-Item C:\\ -Recurse #'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Applications.Add('/api'; Remove-Item", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WebApplicationRemoveScript_IsTolerated_AndRemovesFromSite()
    {
        var app = new WebApplicationModel { Id = "Api", Alias = "/api", Directory = "[INSTALLDIR]api" };
        ExecutionStep step = Single(IisCommandFactory.BuildSteps([], [SiteWithApp("S", "Site", app)], []), "IisApp_Api");

        Assert.NotNull(step.RollbackCommand);
        Assert.NotNull(step.UninstallCommand);

        string removeScript = DecodeEncodedScript(step.RollbackCommand!);
        Assert.Contains("Applications.Remove", removeScript, StringComparison.Ordinal);
        Assert.Contains("catch { [Console]::Error.WriteLine($_.Exception.Message); exit 0 }", removeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void WebApplicationCreateScript_GuardsIisPrerequisite_FailLoud()
    {
        var app = new WebApplicationModel { Id = "Api", Alias = "/api", Directory = "[INSTALLDIR]api" };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], [SiteWithApp("S", "Site", app)], []), "IisApp_Api"));

        Assert.Contains("W3SVC", script, StringComparison.Ordinal);
        Assert.Contains("throw", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WebApplication_WithEmptyAlias_IsSkipped_NoStepGenerated()
    {
        var app = new WebApplicationModel { Id = "Api", Alias = "", Directory = "[INSTALLDIR]api" };

        var ids = IisCommandFactory.BuildSteps([], [SiteWithApp("S", "Site", app)], []).Select(s => s.Id).ToList();

        Assert.DoesNotContain("IisApp_Api", ids); // silently-skipped shapes are blocked by IIS014 at validation
    }

    // ── ssl certificate binding ──────────────────────────────────────────────

    [Fact]
    public void HttpsBindingWithCertificate_LocatesHashInStore_AndBindsIt()
    {
        var cert = new CertificateModel
        {
            Id = "web",
            FindType = CertificateFindType.FindByThumbprint,
            FindValue = "ABCDEF1234567890ABCDEF1234567890ABCDEF12",
            StoreName = CertificateStoreName.My,
            StoreLocation = CertificateStoreLocation.LocalMachine,
        };
        var binding = new WebBindingModel { Protocol = "https", Port = 443, CertificateRef = "web" };
        var sites = new[] { new WebSiteModel { Id = "S", Description = "Site", Directory = "[INSTALLDIR]", Bindings = [binding] } };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, [cert]), "IisCert_S_0"));

        Assert.Contains("Cert:\\LocalMachine\\My", script, StringComparison.Ordinal);
        Assert.Contains("$_.Thumbprint -eq 'ABCDEF1234567890ABCDEF1234567890ABCDEF12'", script, StringComparison.Ordinal);
        Assert.Contains(".CertificateStoreName = 'MY'", script, StringComparison.Ordinal);
        Assert.Contains(".CertificateHash = $__c.GetCertHash()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HttpsBindingWithCertificate_FailsLoud_WhenCertificateAbsentFromStore()
    {
        var cert = new CertificateModel { Id = "web", FindValue = "ABC", StoreName = CertificateStoreName.My };
        var binding = new WebBindingModel { Protocol = "https", Port = 443, CertificateRef = "web" };
        var sites = new[] { new WebSiteModel { Id = "S", Description = "Site", Directory = "[INSTALLDIR]", Bindings = [binding] } };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, [cert]), "IisCert_S_0"));

        // Never a silent unbound HTTPS binding: an absent certificate throws (the cert-bind script is
        // non-tolerant, so this aborts the install).
        Assert.Contains("if ($null -eq $__c) { throw", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificateBinding_HonorsAuthoredStoreAndFindType()
    {
        var cert = new CertificateModel
        {
            Id = "web",
            FindType = CertificateFindType.FindBySubjectName,
            FindValue = "CN=example.com",
            StoreName = CertificateStoreName.Root,
            StoreLocation = CertificateStoreLocation.CurrentUser,
        };
        var binding = new WebBindingModel { Protocol = "https", Port = 443, CertificateRef = "web" };
        var sites = new[] { new WebSiteModel { Id = "S", Description = "Site", Directory = "[INSTALLDIR]", Bindings = [binding] } };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, [cert]), "IisCert_S_0"));

        Assert.Contains("Cert:\\CurrentUser\\Root", script, StringComparison.Ordinal);
        Assert.Contains("$_.Subject -like ('*' + 'CN=example.com' + '*')", script, StringComparison.Ordinal);
        Assert.Contains(".CertificateStoreName = 'Root'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MaliciousCertificateFindValue_IsSingleQuotedLiteral_CannotBreakOut()
    {
        const string evil = "AB'; Remove-Item C:\\ -Recurse #";
        var cert = new CertificateModel { Id = "web", FindValue = evil, StoreName = CertificateStoreName.My };
        var binding = new WebBindingModel { Protocol = "https", Port = 443, CertificateRef = "web" };
        var sites = new[] { new WebSiteModel { Id = "S", Description = "Site", Directory = "[INSTALLDIR]", Bindings = [binding] } };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, [cert]), "IisCert_S_0"));

        Assert.Contains("AB''; Remove-Item C:\\ -Recurse #'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HttpBindingWithoutCertificate_HasNoCertificateBinding()
    {
        var sites = new[] { Site("S", "Site", "[INSTALLDIR]") };

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites, []), "IisSite_S"));

        Assert.DoesNotContain("X509Store", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CertificateHash", script, StringComparison.Ordinal);
    }

    // ── validator / rules ────────────────────────────────────────────────────

    [Fact]
    public void ValidateAppPool_SpecificUserWithPasswordProperty_ReturnsSuccess()
    {
        var pool = new AppPoolModel
        {
            Id = "P", Name = "Pool",
            IdentityType = AppPoolIdentityType.SpecificUser,
            UserName = "domain\\svc",
            PasswordProperty = "IISPWD",
        };

        Assert.True(IisValidator.ValidateAppPool(pool).IsSuccess);
    }

    [Fact]
    public void IIS012_Fires_ForLiteralPassword_NotForPasswordProperty()
    {
        var literal = new AppPoolModel { Id = "L", Name = "LiteralPool", IdentityType = AppPoolIdentityType.SpecificUser, UserName = "u", Password = "p" };
        var secure = new AppPoolModel { Id = "S", Name = "SecurePool", IdentityType = AppPoolIdentityType.SpecificUser, UserName = "u", PasswordProperty = "IISPWD" };

        Assert.True(IisValidator.HasLiteralPassword(literal));
        Assert.False(IisValidator.HasLiteralPassword(secure));

        var engine = new FalkForge.Validation.ValidationEngine(
            new FalkForge.Validation.RuleRegistry(IisRules.Build(() => [], () => new[] { literal, secure }, () => [])));
        var report = engine.Run(MinimalPackage());

        var iis012 = report.Violations.Where(v => v.RuleId.Value == "IIS012").ToList();
        Assert.Single(iis012); // literal pool only — the PasswordProperty pool does not trip it
        Assert.Contains("LiteralPool", iis012[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IIS015_Fires_ForVirtualDirectoryTargetingNonRootApplication_NotForRootApplication()
    {
        var subApp = new WebVirtualDirectoryModel { Id = "V1", Alias = "/reports", Directory = "[INSTALLDIR]reports", WebApplication = "/api" };
        var rootApp = new WebVirtualDirectoryModel { Id = "V2", Alias = "/logs", Directory = "[INSTALLDIR]logs" };
        var site = new WebSiteModel
        {
            Id = "S", Description = "Site", Directory = "[INSTALLDIR]",
            Bindings = [new WebBindingModel { Port = 80 }],
            VirtualDirectories = [subApp, rootApp],
        };

        var engine = new FalkForge.Validation.ValidationEngine(
            new FalkForge.Validation.RuleRegistry(IisRules.Build(() => [site], () => [], () => [])));
        var report = engine.Run(MinimalPackage());

        var iis015 = report.Violations.Where(v => v.RuleId.Value == "IIS015").ToList();
        Assert.Single(iis015); // only the /api-targeting vdir trips it, not the root-application one
        Assert.Contains("/reports", iis015[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IIS013_Fires_ForHttpsCertificateBinding_AsPreProvisionCaveat()
    {
        var binding = new WebBindingModel { Protocol = "https", Port = 443, CertificateRef = "web" };
        var cert = new CertificateModel { Id = "web", FindValue = "ABC", StoreName = CertificateStoreName.My };
        var site = new WebSiteModel
        {
            Id = "S", Description = "Site", Directory = "[INSTALLDIR]",
            Bindings = [binding],
        };

        var engine = new FalkForge.Validation.ValidationEngine(
            new FalkForge.Validation.RuleRegistry(IisRules.Build(() => [site], () => [], () => [cert])));
        var report = engine.Run(MinimalPackage());

        var iis013 = report.Violations.Where(v => v.RuleId.Value == "IIS013").ToList();
        Assert.Single(iis013); // a Warning, not an error — the binding IS wired, the cert must be pre-provisioned
        Assert.Equal(FalkForge.Validation.Severity.Warning, iis013[0].Severity);
        Assert.Contains("web", iis013[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IIS014_Fires_ForWebApplicationWithMissingAlias()
    {
        var app = new WebApplicationModel { Id = "A", Alias = "", Directory = "[INSTALLDIR]api" };
        var site = new WebSiteModel
        {
            Id = "S", Description = "Site", Directory = "[INSTALLDIR]",
            Bindings = [new WebBindingModel { Port = 80 }],
            WebApplications = [app],
        };

        var engine = new FalkForge.Validation.ValidationEngine(
            new FalkForge.Validation.RuleRegistry(IisRules.Build(() => [site], () => [], () => [])));
        var report = engine.Run(MinimalPackage());

        // An empty Alias means IisCommandFactory.BuildSteps silently skips the sub-application — a
        // build-blocking Error, not a silent no-op.
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.RuleId.Value == "IIS014" && v.Severity == FalkForge.Validation.Severity.Error);
    }

    [Fact]
    public void IIS018_Fires_ForWebApplicationWithMissingDirectory()
    {
        var app = new WebApplicationModel { Id = "A", Alias = "/api", Directory = "" };
        var site = new WebSiteModel
        {
            Id = "S", Description = "Site", Directory = "[INSTALLDIR]",
            Bindings = [new WebBindingModel { Port = 80 }],
            WebApplications = [app],
        };

        var engine = new FalkForge.Validation.ValidationEngine(
            new FalkForge.Validation.RuleRegistry(IisRules.Build(() => [site], () => [], () => [])));
        var report = engine.Run(MinimalPackage());

        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.RuleId.Value == "IIS018" && v.Severity == FalkForge.Validation.Severity.Error);
        Assert.Contains("/api", report.Violations.Single(v => v.RuleId.Value == "IIS018").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IIS015_DoesNotFire_WhenParentApplicationIsDefinedOnSite()
    {
        // The vdir targets '/api', which IS authored as a WebApplication on the site → the parent exists at
        // install, so IIS015 must not fire.
        var app = new WebApplicationModel { Id = "Api", Alias = "/api", Directory = "[INSTALLDIR]api" };
        var vdir = new WebVirtualDirectoryModel { Id = "V", Alias = "/reports", Directory = "[INSTALLDIR]reports", WebApplication = "/api" };
        var site = new WebSiteModel
        {
            Id = "S", Description = "Site", Directory = "[INSTALLDIR]",
            Bindings = [new WebBindingModel { Port = 80 }],
            WebApplications = [app],
            VirtualDirectories = [vdir],
        };

        var engine = new FalkForge.Validation.ValidationEngine(
            new FalkForge.Validation.RuleRegistry(IisRules.Build(() => [site], () => [], () => [])));
        var report = engine.Run(MinimalPackage());

        Assert.DoesNotContain(report.Violations, v => v.RuleId.Value == "IIS015");
    }

    [Fact]
    public void IIS016_Fires_ForVirtualDirectoryWithMissingAlias()
    {
        var vdir = new WebVirtualDirectoryModel { Id = "V", Alias = "", Directory = "[INSTALLDIR]reports" };
        var site = new WebSiteModel
        {
            Id = "S", Description = "Site", Directory = "[INSTALLDIR]",
            Bindings = [new WebBindingModel { Port = 80 }],
            VirtualDirectories = [vdir],
        };

        var engine = new FalkForge.Validation.ValidationEngine(
            new FalkForge.Validation.RuleRegistry(IisRules.Build(() => [site], () => [], () => [])));
        var report = engine.Run(MinimalPackage());

        // An empty Alias means IisCommandFactory.BuildSteps silently skips the vdir (never created at
        // install) — this must be a build-blocking Error, not a silent no-op, so the author is told.
        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.RuleId.Value == "IIS016" && v.Severity == FalkForge.Validation.Severity.Error);
    }

    [Fact]
    public void IIS017_Fires_ForVirtualDirectoryWithMissingDirectory()
    {
        var vdir = new WebVirtualDirectoryModel { Id = "V", Alias = "/reports", Directory = "" };
        var site = new WebSiteModel
        {
            Id = "S", Description = "Site", Directory = "[INSTALLDIR]",
            Bindings = [new WebBindingModel { Port = 80 }],
            VirtualDirectories = [vdir],
        };

        var engine = new FalkForge.Validation.ValidationEngine(
            new FalkForge.Validation.RuleRegistry(IisRules.Build(() => [site], () => [], () => [])));
        var report = engine.Run(MinimalPackage());

        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.RuleId.Value == "IIS017" && v.Severity == FalkForge.Validation.Severity.Error);
        Assert.Contains("/reports", report.Violations.Single(v => v.RuleId.Value == "IIS017").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IIS016_And_IIS017_DoNotFire_ForCompleteVirtualDirectory()
    {
        var vdir = new WebVirtualDirectoryModel { Id = "V", Alias = "/reports", Directory = "[INSTALLDIR]reports" };
        var site = new WebSiteModel
        {
            Id = "S", Description = "Site", Directory = "[INSTALLDIR]",
            Bindings = [new WebBindingModel { Port = 80 }],
            VirtualDirectories = [vdir],
        };

        var engine = new FalkForge.Validation.ValidationEngine(
            new FalkForge.Validation.RuleRegistry(IisRules.Build(() => [site], () => [], () => [])));
        var report = engine.Run(MinimalPackage());

        Assert.DoesNotContain(report.Violations, v => v.RuleId.Value is "IIS016" or "IIS017");
    }

    private static FalkForge.Models.PackageModel MinimalPackage() => FalkForge.Testing.InstallerTestHost.BuildPackage(p =>
    {
        p.Name = "App";
        p.Manufacturer = "Corp";
        p.Version = new Version(1, 0, 0);
        p.Files(f => f.Add("app.exe").To(FalkForge.KnownFolder.ProgramFiles / "Corp" / "App"));
    });

    // ── helpers ──────────────────────────────────────────────────────────────

    private static WebSiteModel Site(string id, string desc, string dir, string? appPool = null) => new()
    {
        Id = id,
        Description = desc,
        Directory = dir,
        AppPool = appPool,
        Bindings = [new WebBindingModel { Port = 80 }],
    };

    private static AppPoolModel SpecificUserPool(string id, string name, string user, string? passwordProperty, string? password) => new()
    {
        Id = id,
        Name = name,
        IdentityType = AppPoolIdentityType.SpecificUser,
        UserName = user,
        PasswordProperty = passwordProperty,
        Password = password,
    };

    private static WebSiteModel SiteWithVdir(string siteId, string desc, string vdirId, string alias, string dir) => new()
    {
        Id = siteId,
        Description = desc,
        Directory = "[INSTALLDIR]",
        Bindings = [new WebBindingModel { Port = 80 }],
        VirtualDirectories = [new WebVirtualDirectoryModel { Id = vdirId, Alias = alias, Directory = dir }],
    };

    private static WebSiteModel SiteWithApp(string siteId, string desc, WebApplicationModel app) => new()
    {
        Id = siteId,
        Description = desc,
        Directory = "[INSTALLDIR]",
        Bindings = [new WebBindingModel { Port = 80 }],
        WebApplications = [app],
    };

    private static ExecutionStep Single(IReadOnlyList<ExecutionStep> steps, string id)
        => steps.Single(s => s.Id == id);

    private static string DecodeInstall(ExecutionStep step) => DecodeEncodedScript(step.InstallCommand);

    private static string DecodeEncodedScript(string target)
    {
        const string marker = "-EncodedCommand ";
        int idx = target.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Command is not an -EncodedCommand invocation: {target}");
        int end = target.IndexOf(" \"", idx, StringComparison.Ordinal);
        string base64 = (end >= 0 ? target[(idx + marker.Length)..end] : target[(idx + marker.Length)..]).Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(base64));
    }
}
