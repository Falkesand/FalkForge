using FalkForge.Ui;
using FalkForge.Ui.Abstractions;
using LifecycleDemo.Views;

namespace LifecycleDemo.Pages;

public sealed class ConfigPage : InstallerPage<ConfigView>
{
    private string _dbServer = @".\SQLEXPRESS";
    private string _dbName = "ContosoDataHub";
    private bool _integratedSecurity = true;
    private string _userName = "sa";

    public override string Title => "Configuration";

    public string ServerLabel => "Database Server:";
    public string DatabaseLabel => "Database Name:";
    public string IntegratedSecurityLabel => "Use Integrated Security";
    public string UserNameLabel => "User Name:";
    public string PasswordLabel => "Password:";

    public string DbServer
    {
        get => _dbServer;
        set => SetField(ref _dbServer, value);
    }

    public string DbName
    {
        get => _dbName;
        set => SetField(ref _dbName, value);
    }

    public bool IntegratedSecurity
    {
        get => _integratedSecurity;
        set => SetField(ref _integratedSecurity, value, [nameof(ShowCredentials)]);
    }

    public bool ShowCredentials => !_integratedSecurity;

    public string UserName
    {
        get => _userName;
        set => SetField(ref _userName, value);
    }

    public override PageResult OnNext()
    {
        if (string.IsNullOrWhiteSpace(_dbServer))
            return PageResult.Stay("Database server is required.");
        if (string.IsNullOrWhiteSpace(_dbName))
            return PageResult.Stay("Database name is required.");

        SharedState.Set("DbServer", _dbServer);
        SharedState.Set("DbName", _dbName);
        SharedState.Set("IntegratedSecurity", _integratedSecurity);
        SharedState.Set("DbUserName", _userName);

        using var pw = GetPassword("DbPassword");
        if (!pw.IsEmpty)
            SharedState.SetSensitive("DbPassword", pw.Span);

        return PageResult.Next;
    }
}
