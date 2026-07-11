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

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites), "IisSite_S"));

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

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites), "IisSite_S"));

        Assert.Contains("''; calc #", script, StringComparison.Ordinal); // doubled quote → inert
    }

    [Fact]
    public void PhysicalPath_RidesCustomActionDataChannel_NotBakedIntoScript()
    {
        var sites = new[] { Site("S", "Site", "[INSTALLDIR]web") };
        ExecutionStep step = Single(IisCommandFactory.BuildSteps([], sites), "IisSite_S");

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
        ExecutionStep create = Single(IisCommandFactory.BuildSteps(pools, []), "IisPool_P");

        Assert.Equal("[IISPWD]", create.CustomActionData);
        string script = DecodeInstall(create);
        Assert.Contains("$__pool.ProcessModel.Password = $__arg", script, StringComparison.Ordinal);

        IReadOnlyList<string> hidden = IisCommandFactory.CollectHiddenPropertyNames(pools, []);
        Assert.Contains("IISPWD", hidden);
        Assert.Contains("IisPool_P", hidden);
    }

    [Fact]
    public void SpecificUserLiteral_EmbedsEscapedPassword_InCustomActionData()
    {
        var pools = new[] { SpecificUserPool("P", "Pool", "domain\\svc", passwordProperty: null, password: "Pl4in") };
        ExecutionStep create = Single(IisCommandFactory.BuildSteps(pools, []), "IisPool_P");

        Assert.Equal("Pl4in", create.CustomActionData); // literal (MSI-escaped) — the discouraged path
        // Both the source-less literal and the deferred action property are still hidden from the log.
        Assert.Contains("IisPool_P", IisCommandFactory.CollectHiddenPropertyNames(pools, []));
    }

    [Fact]
    public void NonSpecificUserPool_HasNoCredentialChannel_AndNoHiddenProperties()
    {
        var pools = new[] { new AppPoolModel { Id = "P", Name = "Pool", IdentityType = AppPoolIdentityType.NetworkService } };
        ExecutionStep create = Single(IisCommandFactory.BuildSteps(pools, []), "IisPool_P");

        Assert.Null(create.CustomActionData);
        Assert.Empty(IisCommandFactory.CollectHiddenPropertyNames(pools, []));
    }

    [Fact]
    public void PoolsCreatedFirst_SitesNext_PoolRemovedLast()
    {
        var pools = new[] { new AppPoolModel { Id = "P", Name = "Pool" } };
        var sites = new[] { Site("S", "Site", "[INSTALLDIR]", "P") };

        var ids = IisCommandFactory.BuildSteps(pools, sites).Select(s => s.Id).ToList();

        Assert.True(ids.IndexOf("IisPool_P") < ids.IndexOf("IisSite_S"));
        Assert.True(ids.IndexOf("IisSite_S") < ids.IndexOf("IisPoolDel_P"));
    }

    [Fact]
    public void EveryInstallScript_GuardsIisPrerequisite_FailLoud()
    {
        var pools = new[] { new AppPoolModel { Id = "P", Name = "Pool" } };
        var sites = new[] { Site("S", "Site", "[INSTALLDIR]", "P") };

        foreach (ExecutionStep step in IisCommandFactory.BuildSteps(pools, sites))
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

        string script = DecodeInstall(Single(IisCommandFactory.BuildSteps([], sites), "IisSite_S"));

        Assert.Contains("*:80:", script, StringComparison.Ordinal);
        Assert.Contains("*:8080:a.example", script, StringComparison.Ordinal);
        Assert.Contains("*:8081:b.example", script, StringComparison.Ordinal);
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

    private static ExecutionStep Single(IReadOnlyList<ExecutionStep> steps, string id)
        => steps.Single(s => s.Id == id);

    private static string DecodeInstall(ExecutionStep step)
    {
        const string marker = "-EncodedCommand ";
        string target = step.InstallCommand;
        int idx = target.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"InstallCommand is not an -EncodedCommand invocation: {target}");
        int end = target.IndexOf(" \"", idx, StringComparison.Ordinal);
        string base64 = (end >= 0 ? target[(idx + marker.Length)..end] : target[(idx + marker.Length)..]).Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(base64));
    }
}
