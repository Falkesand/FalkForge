namespace FalkForge.Extensions.Firewall;

public sealed class FirewallRuleBuilder
{
    private FirewallRuleAction _action = FirewallRuleAction.Allow;
    private string? _componentRef;
    private string? _condition;
    private string? _description;
    private FirewallDirection _direction = FirewallDirection.Inbound;
    private string _id = string.Empty;
    private string? _localAddress;
    private string _name = string.Empty;
    private string? _port;
    private FirewallProfile _profile = FirewallProfile.All;
    private string? _program;
    private FirewallProtocol _protocol = FirewallProtocol.Tcp;
    private string? _remoteAddress;
    private string? _remotePort;

    public FirewallRuleBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public FirewallRuleBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public FirewallRuleBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public FirewallRuleBuilder Protocol(FirewallProtocol protocol)
    {
        _protocol = protocol;
        return this;
    }

    public FirewallRuleBuilder Port(string port)
    {
        _port = port;
        return this;
    }

    public FirewallRuleBuilder RemotePort(string remotePort)
    {
        _remotePort = remotePort;
        return this;
    }

    public FirewallRuleBuilder LocalAddress(string localAddress)
    {
        _localAddress = localAddress;
        return this;
    }

    public FirewallRuleBuilder RemoteAddress(string remoteAddress)
    {
        _remoteAddress = remoteAddress;
        return this;
    }

    public FirewallRuleBuilder Program(string program)
    {
        _program = program;
        return this;
    }

    public FirewallRuleBuilder Profile(FirewallProfile profile)
    {
        _profile = profile;
        return this;
    }

    public FirewallRuleBuilder Direction(FirewallDirection direction)
    {
        _direction = direction;
        return this;
    }

    public FirewallRuleBuilder Action(FirewallRuleAction action)
    {
        _action = action;
        return this;
    }

    public FirewallRuleBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    public FirewallRuleBuilder Condition(string condition)
    {
        _condition = condition;
        return this;
    }

    internal FirewallRuleModel Build()
    {
        return new FirewallRuleModel
        {
            Id = _id,
            Name = _name,
            Description = _description,
            Protocol = _protocol,
            Port = _port,
            RemotePort = _remotePort,
            LocalAddress = _localAddress,
            RemoteAddress = _remoteAddress,
            Program = _program,
            Profile = _profile,
            Direction = _direction,
            Action = _action,
            ComponentRef = _componentRef,
            Condition = _condition
        };
    }
}