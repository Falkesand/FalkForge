using System.Text.RegularExpressions;

namespace FalkForge.Extensions.Firewall;

public static partial class FirewallValidator
{
    private static readonly Regex PortFormatRegex = CreatePortFormatRegex();

    public static IReadOnlyList<FirewallValidationError> Validate(IReadOnlyList<FirewallRuleModel> rules)
    {
        var errors = new List<FirewallValidationError>();

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (!string.IsNullOrWhiteSpace(rule.Id) && !seenIds.Add(rule.Id))
            {
                errors.Add(new FirewallValidationError("FWL004", $"Duplicate firewall rule Id '{rule.Id}'."));
            }

            ValidateRule(rule, errors);
        }

        return errors;
    }

    private static void ValidateRule(FirewallRuleModel rule, List<FirewallValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            errors.Add(new FirewallValidationError("FWL001", $"Firewall rule '{rule.Id}' must have a Name."));
        }

        if (string.IsNullOrWhiteSpace(rule.Port) && string.IsNullOrWhiteSpace(rule.Program))
        {
            errors.Add(new FirewallValidationError("FWL002", $"Firewall rule '{rule.Id}' must specify either a Port or a Program."));
        }

        if (!string.IsNullOrWhiteSpace(rule.Port))
        {
            ValidatePort(rule.Port, rule.Id, "port", errors);
        }

        if (!string.IsNullOrWhiteSpace(rule.RemotePort))
        {
            ValidatePort(rule.RemotePort, rule.Id, "remote port", errors);
        }
    }

    private static void ValidatePort(string port, string ruleId, string portLabel, List<FirewallValidationError> errors)
    {
        if (!PortFormatRegex.IsMatch(port))
        {
            errors.Add(new FirewallValidationError("FWL003", $"Firewall rule '{ruleId}' has invalid {portLabel} format '{port}'. Expected a number (e.g. '8080') or a range (e.g. '8080-8090')."));
            return;
        }

        var dashIndex = port.IndexOf('-');
        if (dashIndex < 0)
        {
            var portNumber = int.Parse(port);
            if (portNumber < 1 || portNumber > 65535)
            {
                errors.Add(new FirewallValidationError("FWL003", $"Firewall rule '{ruleId}' has invalid {portLabel} '{port}'. Port must be between 1 and 65535."));
            }
        }
        else
        {
            var start = int.Parse(port[..dashIndex]);
            var end = int.Parse(port[(dashIndex + 1)..]);
            if (start < 1 || start > 65535 || end < 1 || end > 65535)
            {
                errors.Add(new FirewallValidationError("FWL003", $"Firewall rule '{ruleId}' has invalid {portLabel} range '{port}'. Ports must be between 1 and 65535."));
            }
            else if (start > end)
            {
                errors.Add(new FirewallValidationError("FWL003", $"Firewall rule '{ruleId}' has invalid {portLabel} range '{port}'. Start port must not exceed end port."));
            }
        }
    }

    [GeneratedRegex(@"^\d{1,5}(-\d{1,5})?$")]
    private static partial Regex CreatePortFormatRegex();
}
