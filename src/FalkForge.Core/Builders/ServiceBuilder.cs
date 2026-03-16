using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class ServiceBuilder
{
    private readonly string _name;

    private readonly List<ServiceDependencyModel> _typedDependencies = [];
    private readonly List<PermissionModel> _permissions = [];
    private ServiceFailureActionsModel? _failureActions;

    internal ServiceBuilder(string name)
    {
        _name = name;
    }

    public string DisplayName { get; set; } = string.Empty;
    public string Executable { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ServiceStartMode StartMode { get; set; } = ServiceStartMode.Automatic;
    public ServiceAccount Account { get; set; } = ServiceAccount.LocalSystem;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? Arguments { get; set; }
    public List<string> Dependencies { get; } = [];

    private string? _accountProperty;
    private string? _componentCondition;

    public ServiceBuilder DependsOn(string serviceName)
    {
        _typedDependencies.Add(new ServiceDependencyModel
        {
            ServiceName = _name,
            DependsOn = serviceName,
            Group = false
        });
        return this;
    }

    public ServiceBuilder DependsOnGroup(string groupName)
    {
        _typedDependencies.Add(new ServiceDependencyModel
        {
            ServiceName = _name,
            DependsOn = groupName,
            Group = true
        });
        return this;
    }

    public ServiceBuilder AccountProperty(string propertyRef)
    {
        _accountProperty = propertyRef;
        return this;
    }

    public ServiceBuilder Condition(string condition)
    {
        _componentCondition = condition;
        return this;
    }

    public ServiceBuilder Condition(Condition condition)
    {
        _componentCondition = condition.ToString();
        return this;
    }

    public ServiceBuilder Permission(Action<PermissionBuilder> configure)
    {
        var builder = new PermissionBuilder(_name);
        builder.ForTable("ServiceInstall");
        configure(builder);
        _permissions.Add(builder.Build());
        return this;
    }

    public ServiceBuilder FailureActions(Action<ServiceFailureActionsBuilder> configure)
    {
        var builder = new ServiceFailureActionsBuilder();
        configure(builder);
        _failureActions = builder.Build();
        return this;
    }

    internal ServiceModel Build()
    {
        return new ServiceModel
        {
            Name = _name,
            DisplayName = string.IsNullOrEmpty(DisplayName) ? _name : DisplayName,
            Executable = Executable,
            Description = Description,
            StartMode = StartMode,
            Account = Account,
            UserName = UserName,
            Password = Password,
            Arguments = Arguments,
            AccountProperty = _accountProperty,
            ComponentCondition = _componentCondition,
            Dependencies = Dependencies,
            TypedDependencies = _typedDependencies,
            FailureActions = _failureActions,
            Permissions = _permissions
        };
    }
}