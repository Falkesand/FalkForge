namespace FalkForge.Extensions.Firewall;

public sealed class FirewallRuleModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public FirewallProtocol Protocol { get; init; } = FirewallProtocol.Tcp;
    public string? Port { get; init; }
    public string? RemotePort { get; init; }
    public string? LocalAddress { get; init; }
    public string? RemoteAddress { get; init; }
    public string? Program { get; init; }
    public FirewallProfile Profile { get; init; } = FirewallProfile.All;
    public FirewallDirection Direction { get; init; } = FirewallDirection.Inbound;
    public FirewallRuleAction Action { get; init; } = FirewallRuleAction.Allow;
    public string? ComponentRef { get; init; }
    public string? Condition { get; init; }
}
