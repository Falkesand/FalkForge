using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Built-in rules for <see cref="ServiceModel"/> (SVC001-005, SVC009-011),
/// <see cref="ServiceControlModel"/> (SCT001-002), and service dependencies (SDP001).
/// </summary>
public static class ServiceRules
{
    /// <summary>SVC001 — Service Name is required.</summary>
    public static readonly ValidationRule Svc001_NameRequired = new(
        new RuleId("SVC001"),
        Severity.Error,
        ModelSection.Service,
        "Service Name required",
        "Every service must have a non-empty Name.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Services.Count; i++)
            {
                var svc = ctx.Package.Services[i];
                if (string.IsNullOrWhiteSpace(svc.Name))
                    violations.Add(new Violation(
                        new RuleId("SVC001"),
                        Severity.Error,
                        ModelPath.Root.Field("Services").Index(i).Field("Name"),
                        "Service Name is required."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SVC002 — Service Executable is required.</summary>
    public static readonly ValidationRule Svc002_ExecutableRequired = new(
        new RuleId("SVC002"),
        Severity.Error,
        ModelSection.Service,
        "Service Executable required",
        "Every service must have a non-empty Executable.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Services.Count; i++)
            {
                var svc = ctx.Package.Services[i];
                if (string.IsNullOrWhiteSpace(svc.Executable))
                    violations.Add(new Violation(
                        new RuleId("SVC002"),
                        Severity.Error,
                        ModelPath.Root.Field("Services").Index(i).Field("Executable"),
                        $"Service '{svc.Name}' must have an Executable."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SVC003 — User account requires UserName.</summary>
    public static readonly ValidationRule Svc003_UserAccountRequiresUserName = new(
        new RuleId("SVC003"),
        Severity.Error,
        ModelSection.Service,
        "User account requires UserName",
        "A service with Account=User must specify a UserName.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Services.Count; i++)
            {
                var svc = ctx.Package.Services[i];
                if (svc.Account == ServiceAccount.User && string.IsNullOrWhiteSpace(svc.UserName))
                    violations.Add(new Violation(
                        new RuleId("SVC003"),
                        Severity.Error,
                        ModelPath.Root.Field("Services").Index(i).Field("UserName"),
                        $"Service '{svc.Name}' uses User account but no UserName specified."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SVC004 — Service name must not exceed 256 characters.</summary>
    public static readonly ValidationRule Svc004_NameLength = new(
        new RuleId("SVC004"),
        Severity.Error,
        ModelSection.Service,
        "Service name length",
        "Service name is limited to 256 characters by the Windows Service Control Manager.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Services.Count; i++)
            {
                var svc = ctx.Package.Services[i];
                if (svc.Name.Length > 256)
                    violations.Add(new Violation(
                        new RuleId("SVC004"),
                        Severity.Error,
                        ModelPath.Root.Field("Services").Index(i).Field("Name"),
                        $"Service name '{svc.Name}' exceeds 256 characters."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SVC005 — Plaintext password warning.</summary>
    public static readonly ValidationRule Svc005_PlaintextPasswordWarning = new(
        new RuleId("SVC005"),
        Severity.Warning,
        ModelSection.Service,
        "Plaintext service password",
        "Service passwords stored as plaintext in the MSI are visible in the installer package. Use a managed service account or secure storage instead.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Services.Count; i++)
            {
                var svc = ctx.Package.Services[i];
                if (!string.IsNullOrEmpty(svc.Password))
                    violations.Add(new Violation(
                        new RuleId("SVC005"),
                        Severity.Warning,
                        ModelPath.Root.Field("Services").Index(i).Field("Password"),
                        $"Service '{svc.Name}' has a plaintext password. Consider using a managed service account or store the password securely."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SVC009 — Empty Arguments string should be null.</summary>
    public static readonly ValidationRule Svc009_EmptyArgumentsWarning = new(
        new RuleId("SVC009"),
        Severity.Warning,
        ModelSection.Service,
        "Empty Arguments should be null",
        "An empty Arguments string and null Arguments produce different MSI output. Use null to omit arguments entirely.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Services.Count; i++)
            {
                var svc = ctx.Package.Services[i];
                if (svc.Arguments is not null && svc.Arguments.Length == 0)
                    violations.Add(new Violation(
                        new RuleId("SVC009"),
                        Severity.Warning,
                        ModelPath.Root.Field("Services").Index(i).Field("Arguments"),
                        $"Service '{svc.Name}' has empty Arguments. Use null to omit arguments."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SVC010 — AccountProperty conflicts with UserName.</summary>
    public static readonly ValidationRule Svc010_AccountPropertyConflict = new(
        new RuleId("SVC010"),
        Severity.Warning,
        ModelSection.Service,
        "AccountProperty conflicts with UserName",
        "When both AccountProperty and UserName are set for a User-account service, AccountProperty takes precedence and UserName is ignored.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Services.Count; i++)
            {
                var svc = ctx.Package.Services[i];
                if (svc.AccountProperty is not null
                    && svc.Account == ServiceAccount.User
                    && !string.IsNullOrWhiteSpace(svc.UserName))
                    violations.Add(new Violation(
                        new RuleId("SVC010"),
                        Severity.Warning,
                        ModelPath.Root.Field("Services").Index(i).Field("AccountProperty"),
                        $"Service '{svc.Name}' has both AccountProperty and UserName set. AccountProperty will take precedence."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SVC011 — Empty ComponentCondition must be null.</summary>
    public static readonly ValidationRule Svc011_EmptyComponentCondition = new(
        new RuleId("SVC011"),
        Severity.Error,
        ModelSection.Service,
        "Empty ComponentCondition must be null",
        "An empty-string ComponentCondition is invalid. Use null to omit the condition.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Services.Count; i++)
            {
                var svc = ctx.Package.Services[i];
                if (svc.ComponentCondition is not null && svc.ComponentCondition.Length == 0)
                    violations.Add(new Violation(
                        new RuleId("SVC011"),
                        Severity.Error,
                        ModelPath.Root.Field("Services").Index(i).Field("ComponentCondition"),
                        $"Service '{svc.Name}' has empty ComponentCondition. Use null to omit condition."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SCT001 — ServiceControl ServiceName is required.</summary>
    public static readonly ValidationRule Sct001_ServiceNameRequired = new(
        new RuleId("SCT001"),
        Severity.Error,
        ModelSection.Service,
        "ServiceControl ServiceName required",
        "Every ServiceControl entry must reference a non-empty service name.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.ServiceControls.Count; i++)
            {
                var ctrl = ctx.Package.ServiceControls[i];
                if (string.IsNullOrWhiteSpace(ctrl.ServiceName))
                    violations.Add(new Violation(
                        new RuleId("SCT001"),
                        Severity.Error,
                        ModelPath.Root.Field("ServiceControls").Index(i).Field("ServiceName"),
                        $"ServiceControl '{ctrl.Id}' must have a ServiceName."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SCT002 — ServiceControl must have at least one event.</summary>
    public static readonly ValidationRule Sct002_EventsRequired = new(
        new RuleId("SCT002"),
        Severity.Error,
        ModelSection.Service,
        "ServiceControl events required",
        "A ServiceControl entry with no events configured has no effect.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.ServiceControls.Count; i++)
            {
                var ctrl = ctx.Package.ServiceControls[i];
                if (ctrl.Events == ServiceControlEvent.None)
                    violations.Add(new Violation(
                        new RuleId("SCT002"),
                        Severity.Error,
                        ModelPath.Root.Field("ServiceControls").Index(i).Field("Events"),
                        $"ServiceControl '{ctrl.Id}' must have at least one event specified."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SDP001 — Service dependency DependsOn value is required.</summary>
    public static readonly ValidationRule Sdp001_DependsOnRequired = new(
        new RuleId("SDP001"),
        Severity.Error,
        ModelSection.Service,
        "Service dependency DependsOn required",
        "Every typed service dependency must specify the name of the service it depends on.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Services.Count; i++)
            {
                var svc = ctx.Package.Services[i];
                for (var j = 0; j < svc.TypedDependencies.Count; j++)
                {
                    var dep = svc.TypedDependencies[j];
                    if (string.IsNullOrWhiteSpace(dep.DependsOn))
                        violations.Add(new Violation(
                            new RuleId("SDP001"),
                            Severity.Error,
                            ModelPath.Root.Field("Services").Index(i).Field("TypedDependencies").Index(j).Field("DependsOn"),
                            $"Service '{svc.Name}' has a dependency with no DependsOn value."));
                }
            }
            return violations.ToImmutable();
        });

    /// <summary>
    /// All service-related rules in order, ready to be included in a <see cref="RuleRegistry"/>.
    /// </summary>
    public static readonly ValidationRule[] All =
    [
        Svc001_NameRequired,
        Svc002_ExecutableRequired,
        Svc003_UserAccountRequiresUserName,
        Svc004_NameLength,
        Svc005_PlaintextPasswordWarning,
        Svc009_EmptyArgumentsWarning,
        Svc010_AccountPropertyConflict,
        Svc011_EmptyComponentCondition,
        Sct001_ServiceNameRequired,
        Sct002_EventsRequired,
        Sdp001_DependsOnRequired
    ];
}
