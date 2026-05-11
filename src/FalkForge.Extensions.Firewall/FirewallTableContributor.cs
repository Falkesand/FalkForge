using FalkForge.Extensibility;

namespace FalkForge.Extensions.Firewall;

public sealed class FirewallTableContributor : IMsiTableContributor
{
    private readonly List<FirewallRuleModel> _rules = [];

    public IReadOnlyList<FirewallRuleModel> Rules => _rules;

    public string TableName => "WixFirewallException";

    /// <inheritdoc/>
    public ITableReadSchema? ReadSchema => FirewallTableReadSchema.Instance;

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>();

        foreach (var rule in _rules)
        {
            var row = new MsiTableRow()
                .Set("Name", rule.Name)
                .Set("RemoteAddresses", rule.RemoteAddress)
                .Set("Port", rule.Port)
                .Set("Protocol", MapProtocol(rule.Protocol))
                .Set("Program", rule.Program)
                .Set("Profile", (int)rule.Profile)
                .Set("Direction", MapDirection(rule.Direction))
                .Set("Action", MapAction(rule.Action))
                .Set("Component_", rule.ComponentRef)
                .Set("Description", rule.Description)
                .Set("Condition", rule.Condition);

            rows.Add(row);
        }

        return rows;
    }

    public void AddRule(FirewallRuleModel rule)
    {
        _rules.Add(rule);
    }

    private static int MapProtocol(FirewallProtocol protocol)
    {
        return protocol switch
        {
            FirewallProtocol.Tcp => 6,
            FirewallProtocol.Udp => 17,
            FirewallProtocol.Any => 256,
            _ => 6
        };
    }

    private static int MapDirection(FirewallDirection direction)
    {
        return direction switch
        {
            FirewallDirection.Inbound => 1,
            FirewallDirection.Outbound => 2,
            _ => 1
        };
    }

    private static int MapAction(FirewallRuleAction action)
    {
        return action switch
        {
            FirewallRuleAction.Allow => 1,
            FirewallRuleAction.Block => 0,
            _ => 1
        };
    }
}