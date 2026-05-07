using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Runs a <see cref="RuleRegistry"/> against a package model.
/// Builds a <see cref="RuleContext"/> once per run, then iterates the registry
/// accumulating violations into a <see cref="ValidationReport"/>.
/// </summary>
public sealed class ValidationEngine
{
    private readonly RuleRegistry _registry;

    public ValidationEngine(RuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>Runs all rules in the registry against <paramref name="package"/>.</summary>
    public ValidationReport Run(PackageModel package, ValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        options ??= ValidationOptions.Default;

        var registry = options.Rules ?? _registry;
        if (registry.Rules.IsEmpty)
            return ValidationReport.Empty;

        var ctx = RuleContextBuilder.Build(package);
        var violations = ImmutableArray.CreateBuilder<Violation>();
        var warningsAsErrors = options.WarningsAsErrors;
        var ignored = options.IgnoredRules;

        var stopEarly = false;
        foreach (var rule in registry.Rules)
        {
            if (stopEarly)
                break;

            if (ignored.Count > 0 && ignored.Contains(rule.Id.Value))
                continue;

            foreach (var violation in rule.Evaluate(ctx))
            {
                var v = warningsAsErrors && violation.Severity == Severity.Warning
                    ? violation with { Severity = Severity.Error }
                    : violation;

                violations.Add(v);

                if (options.StopOnFirstError && v.Severity == Severity.Error)
                {
                    stopEarly = true;
                    break;
                }
            }
        }

        return violations.Count == 0
            ? ValidationReport.Empty
            : new ValidationReport(violations.ToImmutable());
    }
}
