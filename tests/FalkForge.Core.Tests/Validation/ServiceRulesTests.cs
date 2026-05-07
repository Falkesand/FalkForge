using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

/// <summary>
/// Per-rule isolated tests for ServiceRules (SVC001-005, SVC009-011, SCT001-002, SDP001).
/// Each test calls a rule directly via RuleContext.ForTest — no full orchestrator.
/// </summary>
public sealed class ServiceRulesTests
{
    private static RuleContext Ctx(PackageModel pkg) => RuleContext.ForTest(pkg);

    private static PackageModel PkgWithService(ServiceModel svc) => new()
    {
        Name = "App",
        Manufacturer = "Corp",
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid(),
        Services = [svc]
    };

    private static PackageModel PkgWithControl(ServiceControlModel ctrl) => new()
    {
        Name = "App",
        Manufacturer = "Corp",
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid(),
        ServiceControls = [ctrl]
    };

    private static ServiceModel ValidService(string name = "MySvc") => new()
    {
        Name = name,
        DisplayName = name,
        Executable = "svc.exe"
    };

    // ── SVC001 — Service Name required ──────────────────────────────────────

    [Fact]
    public void Svc001_empty_name_yields_error()
    {
        var svc = new ServiceModel { Name = "", DisplayName = "X", Executable = "x.exe" };
        var violations = ServiceRules.Svc001_NameRequired.Evaluate(Ctx(PkgWithService(svc))).ToList();

        Assert.Single(violations);
        Assert.Equal("SVC001", violations[0].RuleId.Value);
        Assert.Equal(Severity.Error, violations[0].Severity);
    }

    [Fact]
    public void Svc001_whitespace_name_yields_error()
    {
        var svc = new ServiceModel { Name = "  ", DisplayName = "X", Executable = "x.exe" };
        Assert.Single(ServiceRules.Svc001_NameRequired.Evaluate(Ctx(PkgWithService(svc))).ToList());
    }

    [Fact]
    public void Svc001_valid_name_yields_no_violations()
    {
        Assert.Empty(ServiceRules.Svc001_NameRequired.Evaluate(Ctx(PkgWithService(ValidService()))));
    }

    // ── SVC002 — Service Executable required ────────────────────────────────

    [Fact]
    public void Svc002_empty_executable_yields_error()
    {
        var svc = new ServiceModel { Name = "S", DisplayName = "S", Executable = "" };
        var violations = ServiceRules.Svc002_ExecutableRequired.Evaluate(Ctx(PkgWithService(svc))).ToList();

        Assert.Single(violations);
        Assert.Equal("SVC002", violations[0].RuleId.Value);
    }

    [Fact]
    public void Svc002_valid_executable_yields_no_violations()
    {
        Assert.Empty(ServiceRules.Svc002_ExecutableRequired.Evaluate(Ctx(PkgWithService(ValidService()))));
    }

    // ── SVC003 — User account requires UserName ──────────────────────────────

    [Fact]
    public void Svc003_user_account_without_username_yields_error()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            Account = ServiceAccount.User,
            UserName = null
        };
        var violations = ServiceRules.Svc003_UserAccountRequiresUserName.Evaluate(Ctx(PkgWithService(svc))).ToList();

        Assert.Single(violations);
        Assert.Equal("SVC003", violations[0].RuleId.Value);
    }

    [Fact]
    public void Svc003_user_account_with_username_yields_no_violations()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            Account = ServiceAccount.User,
            UserName = "DOMAIN\\svcaccount"
        };
        Assert.Empty(ServiceRules.Svc003_UserAccountRequiresUserName.Evaluate(Ctx(PkgWithService(svc))));
    }

    [Fact]
    public void Svc003_local_system_without_username_yields_no_violations()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            Account = ServiceAccount.LocalSystem
        };
        Assert.Empty(ServiceRules.Svc003_UserAccountRequiresUserName.Evaluate(Ctx(PkgWithService(svc))));
    }

    // ── SVC004 — Service name length ─────────────────────────────────────────

    [Fact]
    public void Svc004_name_over_256_chars_yields_error()
    {
        var svc = new ServiceModel
        {
            Name = new string('X', 257),
            DisplayName = "X",
            Executable = "x.exe"
        };
        var violations = ServiceRules.Svc004_NameLength.Evaluate(Ctx(PkgWithService(svc))).ToList();

        Assert.Single(violations);
        Assert.Equal("SVC004", violations[0].RuleId.Value);
    }

    [Fact]
    public void Svc004_name_exactly_256_chars_yields_no_violations()
    {
        var svc = new ServiceModel
        {
            Name = new string('X', 256),
            DisplayName = "X",
            Executable = "x.exe"
        };
        Assert.Empty(ServiceRules.Svc004_NameLength.Evaluate(Ctx(PkgWithService(svc))));
    }

    // ── SVC005 — Plaintext password warning ──────────────────────────────────

    [Fact]
    public void Svc005_plaintext_password_yields_warning()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            Password = "secret123"
        };
        var violations = ServiceRules.Svc005_PlaintextPasswordWarning.Evaluate(Ctx(PkgWithService(svc))).ToList();

        Assert.Single(violations);
        Assert.Equal("SVC005", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Svc005_no_password_yields_no_violations()
    {
        Assert.Empty(ServiceRules.Svc005_PlaintextPasswordWarning.Evaluate(Ctx(PkgWithService(ValidService()))));
    }

    // ── SVC009 — Empty Arguments warning ────────────────────────────────────

    [Fact]
    public void Svc009_empty_arguments_string_yields_warning()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            Arguments = ""
        };
        var violations = ServiceRules.Svc009_EmptyArgumentsWarning.Evaluate(Ctx(PkgWithService(svc))).ToList();

        Assert.Single(violations);
        Assert.Equal("SVC009", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Svc009_null_arguments_yields_no_violations()
    {
        var svc = new ServiceModel { Name = "S", DisplayName = "S", Executable = "s.exe", Arguments = null };
        Assert.Empty(ServiceRules.Svc009_EmptyArgumentsWarning.Evaluate(Ctx(PkgWithService(svc))));
    }

    [Fact]
    public void Svc009_non_empty_arguments_yields_no_violations()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            Arguments = "--verbose"
        };
        Assert.Empty(ServiceRules.Svc009_EmptyArgumentsWarning.Evaluate(Ctx(PkgWithService(svc))));
    }

    // ── SVC010 — AccountProperty + UserName conflict ──────────────────────────

    [Fact]
    public void Svc010_both_accountproperty_and_username_yields_warning()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            Account = ServiceAccount.User,
            UserName = "DOMAIN\\svc",
            AccountProperty = "SVC_ACCOUNT"
        };
        var violations = ServiceRules.Svc010_AccountPropertyConflict.Evaluate(Ctx(PkgWithService(svc))).ToList();

        Assert.Single(violations);
        Assert.Equal("SVC010", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Svc010_only_username_yields_no_violations()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            Account = ServiceAccount.User,
            UserName = "DOMAIN\\svc"
        };
        Assert.Empty(ServiceRules.Svc010_AccountPropertyConflict.Evaluate(Ctx(PkgWithService(svc))));
    }

    // ── SVC011 — Empty ComponentCondition error ───────────────────────────────

    [Fact]
    public void Svc011_empty_component_condition_yields_error()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            ComponentCondition = ""
        };
        var violations = ServiceRules.Svc011_EmptyComponentCondition.Evaluate(Ctx(PkgWithService(svc))).ToList();

        Assert.Single(violations);
        Assert.Equal("SVC011", violations[0].RuleId.Value);
        Assert.Equal(Severity.Error, violations[0].Severity);
    }

    [Fact]
    public void Svc011_null_component_condition_yields_no_violations()
    {
        var svc = new ServiceModel { Name = "S", DisplayName = "S", Executable = "s.exe", ComponentCondition = null };
        Assert.Empty(ServiceRules.Svc011_EmptyComponentCondition.Evaluate(Ctx(PkgWithService(svc))));
    }

    // ── SCT001 — ServiceControl ServiceName required ──────────────────────────

    [Fact]
    public void Sct001_empty_service_name_yields_error()
    {
        var ctrl = new ServiceControlModel { Id = "SC1", ServiceName = "" };
        var violations = ServiceRules.Sct001_ServiceNameRequired.Evaluate(Ctx(PkgWithControl(ctrl))).ToList();

        Assert.Single(violations);
        Assert.Equal("SCT001", violations[0].RuleId.Value);
        Assert.Equal(Severity.Error, violations[0].Severity);
    }

    [Fact]
    public void Sct001_valid_service_name_yields_no_violations()
    {
        var ctrl = new ServiceControlModel
        {
            Id = "SC1",
            ServiceName = "MySvc",
            Events = ServiceControlEvent.StartOnInstall
        };
        Assert.Empty(ServiceRules.Sct001_ServiceNameRequired.Evaluate(Ctx(PkgWithControl(ctrl))));
    }

    // ── SCT002 — ServiceControl must have at least one event ─────────────────

    [Fact]
    public void Sct002_no_events_yields_error()
    {
        var ctrl = new ServiceControlModel { Id = "SC1", ServiceName = "MySvc", Events = ServiceControlEvent.None };
        var violations = ServiceRules.Sct002_EventsRequired.Evaluate(Ctx(PkgWithControl(ctrl))).ToList();

        Assert.Single(violations);
        Assert.Equal("SCT002", violations[0].RuleId.Value);
    }

    [Fact]
    public void Sct002_with_events_yields_no_violations()
    {
        var ctrl = new ServiceControlModel
        {
            Id = "SC1",
            ServiceName = "MySvc",
            Events = ServiceControlEvent.StartOnInstall | ServiceControlEvent.StopOnUninstall
        };
        Assert.Empty(ServiceRules.Sct002_EventsRequired.Evaluate(Ctx(PkgWithControl(ctrl))));
    }

    // ── SDP001 — Service dependency DependsOn required ───────────────────────

    [Fact]
    public void Sdp001_empty_depends_on_yields_error()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            TypedDependencies = [new ServiceDependencyModel { ServiceName = "S", DependsOn = "" }]
        };
        var violations = ServiceRules.Sdp001_DependsOnRequired.Evaluate(Ctx(PkgWithService(svc))).ToList();

        Assert.Single(violations);
        Assert.Equal("SDP001", violations[0].RuleId.Value);
    }

    [Fact]
    public void Sdp001_valid_depends_on_yields_no_violations()
    {
        var svc = new ServiceModel
        {
            Name = "S", DisplayName = "S", Executable = "s.exe",
            TypedDependencies = [new ServiceDependencyModel { ServiceName = "S", DependsOn = "OtherSvc" }]
        };
        Assert.Empty(ServiceRules.Sdp001_DependsOnRequired.Evaluate(Ctx(PkgWithService(svc))));
    }

    [Fact]
    public void Sdp001_no_typed_dependencies_yields_no_violations()
    {
        Assert.Empty(ServiceRules.Sdp001_DependsOnRequired.Evaluate(Ctx(PkgWithService(ValidService()))));
    }
}
