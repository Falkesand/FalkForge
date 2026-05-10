using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FalkForge.Validation;

namespace FalkForge.Extensions.Firewall;

/// <summary>
/// Rules-as-data for the Firewall extension (FWL001–FWL004).
/// Rules are built per-extension-instance so they can close over the rule list owned by that instance.
/// </summary>
public static partial class FirewallRules
{
    [GeneratedRegex(@"^\d{1,5}(-\d{1,5})?$")]
    private static partial Regex PortFormatRegex();

    /// <summary>
    /// Builds the full set of <see cref="ValidationRule"/> instances for one <see cref="FirewallExtension"/>.
    /// Each rule closes over <paramref name="getRules"/> so it always sees the current rule list at validation time.
    /// </summary>
    public static ImmutableArray<ValidationRule> Build(Func<IReadOnlyList<FirewallRuleModel>> getRules)
    {
        return
        [
            new ValidationRule(
                new RuleId("FWL001"),
                Severity.Error,
                ModelSection.Extension_Firewall,
                "Firewall rule must have a Name",
                "Every firewall rule must specify a non-empty Name.",
                _ => getRules()
                    .Where(r => string.IsNullOrWhiteSpace(r.Name))
                    .Select(r => new Violation(
                        new RuleId("FWL001"), Severity.Error,
                        ModelPath.Root.Field("FirewallRule").Field(r.Id),
                        $"Firewall rule '{r.Id}' must have a Name."))),

            new ValidationRule(
                new RuleId("FWL002"),
                Severity.Error,
                ModelSection.Extension_Firewall,
                "Firewall rule must specify Port or Program",
                "Every firewall rule must specify either a Port or a Program.",
                _ => getRules()
                    .Where(r => string.IsNullOrWhiteSpace(r.Port) && string.IsNullOrWhiteSpace(r.Program))
                    .Select(r => new Violation(
                        new RuleId("FWL002"), Severity.Error,
                        ModelPath.Root.Field("FirewallRule").Field(r.Id),
                        $"Firewall rule '{r.Id}' must specify either a Port or a Program."))),

            new ValidationRule(
                new RuleId("FWL003"),
                Severity.Error,
                ModelSection.Extension_Firewall,
                "Firewall rule port format is invalid",
                "Port values must be a number (e.g. '8080') or a range (e.g. '8080-8090') between 1 and 65535.",
                _ => getRules().SelectMany(r => ValidatePorts(r))),

            new ValidationRule(
                new RuleId("FWL004"),
                Severity.Error,
                ModelSection.Extension_Firewall,
                "Duplicate firewall rule Id",
                "Each firewall rule must have a unique Id.",
                _ =>
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    return getRules()
                        .Where(r => !string.IsNullOrWhiteSpace(r.Id) && !seen.Add(r.Id))
                        .Select(r => new Violation(
                            new RuleId("FWL004"), Severity.Error,
                            ModelPath.Root.Field("FirewallRule").Field(r.Id),
                            $"Duplicate firewall rule Id '{r.Id}'."));
                }),
        ];
    }

    private static IEnumerable<Violation> ValidatePorts(FirewallRuleModel rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.Port))
        {
            foreach (var v in ValidatePortValue(rule.Port, rule.Id, "port"))
                yield return v;
        }

        if (!string.IsNullOrWhiteSpace(rule.RemotePort))
        {
            foreach (var v in ValidatePortValue(rule.RemotePort, rule.Id, "remote port"))
                yield return v;
        }
    }

    private static IEnumerable<Violation> ValidatePortValue(string port, string ruleId, string label)
    {
        if (!PortFormatRegex().IsMatch(port))
        {
            yield return new Violation(
                new RuleId("FWL003"), Severity.Error,
                ModelPath.Root.Field("FirewallRule").Field(ruleId),
                $"Firewall rule '{ruleId}' has invalid {label} format '{port}'. Expected a number (e.g. '8080') or a range (e.g. '8080-8090').");
            yield break;
        }

        var dashIndex = port.IndexOf('-');
        if (dashIndex < 0)
        {
            var num = int.Parse(port);
            if (num < 1 || num > 65535)
                yield return new Violation(
                    new RuleId("FWL003"), Severity.Error,
                    ModelPath.Root.Field("FirewallRule").Field(ruleId),
                    $"Firewall rule '{ruleId}' has invalid {label} '{port}'. Port must be between 1 and 65535.");
        }
        else
        {
            var start = int.Parse(port[..dashIndex]);
            var end = int.Parse(port[(dashIndex + 1)..]);
            if (start < 1 || start > 65535 || end < 1 || end > 65535)
                yield return new Violation(
                    new RuleId("FWL003"), Severity.Error,
                    ModelPath.Root.Field("FirewallRule").Field(ruleId),
                    $"Firewall rule '{ruleId}' has invalid {label} range '{port}'. Ports must be between 1 and 65535.");
            else if (start > end)
                yield return new Violation(
                    new RuleId("FWL003"), Severity.Error,
                    ModelPath.Root.Field("FirewallRule").Field(ruleId),
                    $"Firewall rule '{ruleId}' has invalid {label} range '{port}'. Start port must not exceed end port.");
        }
    }
}
