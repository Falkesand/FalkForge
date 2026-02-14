namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class ServiceBuilder
{
    private readonly string _name;
    private ServiceFailureActionsModel? _failureActions;

    internal ServiceBuilder(string name) => _name = name;

    public string DisplayName { get; set; } = string.Empty;
    public string Executable { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ServiceStartMode StartMode { get; set; } = ServiceStartMode.Automatic;
    public ServiceAccount Account { get; set; } = ServiceAccount.LocalSystem;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public List<string> Dependencies { get; } = [];

    public ServiceBuilder FailureActions(Action<ServiceFailureActionsBuilder> configure)
    {
        var builder = new ServiceFailureActionsBuilder();
        configure(builder);
        _failureActions = builder.Build();
        return this;
    }

    internal ServiceModel Build() => new()
    {
        Name = _name,
        DisplayName = string.IsNullOrEmpty(DisplayName) ? _name : DisplayName,
        Executable = Executable,
        Description = Description,
        StartMode = StartMode,
        Account = Account,
        UserName = UserName,
        Password = Password,
        Dependencies = Dependencies,
        FailureActions = _failureActions
    };
}
