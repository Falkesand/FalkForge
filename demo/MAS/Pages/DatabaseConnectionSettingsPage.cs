using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class DatabaseConnectionSettingsPage : MasPageBase<DatabaseConnectionSettingsView>
{
    private string _databaseServer = @".\SQLEXPRESS";
    private string _databaseName = "MultiAccess";
    private bool _integratedSecurity = true;
    private string _userName = "AUSR_AptusWeb";
    private string _password = string.Empty;
    private bool _skipTest;

    public override string Title => "Database Connection Settings";
    public override string? Subtitle => "Please enter SQL database information to continue";

    public string DatabaseServer
    {
        get => _databaseServer;
        set => SetField(ref _databaseServer, value);
    }

    public string DatabaseName
    {
        get => _databaseName;
        set => SetField(ref _databaseName, value);
    }

    public bool IntegratedSecurity
    {
        get => _integratedSecurity;
        set
        {
            if (SetField(ref _integratedSecurity, value))
                OnPropertyChanged(nameof(ShowCredentials));
        }
    }

    public bool ShowCredentials => !_integratedSecurity;

    public string UserName
    {
        get => _userName;
        set => SetField(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    public bool SkipTest
    {
        get => _skipTest;
        set => SetField(ref _skipTest, value);
    }

    public string WarningText => "The user will not be created if the user don't exists. For help please see the manual for MultiAccess";

    public override PageResult OnNext()
    {
        SharedState.Set("DatabaseServer", _databaseServer);
        SharedState.Set("DatabaseName", _databaseName);
        SharedState.Set("IntegratedSecurity", _integratedSecurity);
        SharedState.Set("DbUserName", _userName);
        SharedState.Set("DbPassword", _password);
        return PageResult.GoTo<MultiServerAdvancedSettingsPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<AdvancedInstallDirMultiServerExPage>();

    public override Task OnNavigatedToAsync()
    {
        var server = SharedState.Get<string>("DatabaseServer");
        if (!string.IsNullOrEmpty(server))
            DatabaseServer = server;
        var dbName = SharedState.Get<string>("DatabaseName");
        if (!string.IsNullOrEmpty(dbName))
            DatabaseName = dbName;
        return Task.CompletedTask;
    }
}
