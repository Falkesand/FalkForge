using FalkForge.Plugins.Sql;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class DatabaseConnectionSettingsPage : MasPageBase<DatabaseConnectionSettingsView>
{
    private string _databaseServer = @".\SQLEXPRESS";
    private string _databaseName = "MultiAccess";
    private bool _integratedSecurity = true;
    private string _userName = "AUSR_AptusWeb";
    private bool _skipTest;
    private string _testResult = string.Empty;

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
        set => SetField(ref _integratedSecurity, value, [nameof(ShowCredentials)]);
    }

    public bool ShowCredentials => !_integratedSecurity;

    public string UserName
    {
        get => _userName;
        set => SetField(ref _userName, value);
    }

    public bool SkipTest
    {
        get => _skipTest;
        set => SetField(ref _skipTest, value);
    }

    public string TestResult
    {
        get => _testResult;
        set => SetField(ref _testResult, value);
    }

    public string WarningText => "The user will not be created if the user don't exists. For help please see the manual for MultiAccess";

    public async Task TestConnectionAsync()
    {
        var tester = PluginServices.GetService<IConnectionTester>();
        if (tester is null) return;

        TestResult = "Testing...";
        using var pw = GetPassword("DbPassword");
        var passwordStr = pw.IsEmpty ? string.Empty : System.Text.Encoding.UTF8.GetString(pw.Span);
        var result = await tester.TestConnectionAsync(
            DatabaseServer, DatabaseName, IntegratedSecurity, UserName, passwordStr);
        TestResult = result.IsSuccess
            ? "Connection successful!"
            : $"Failed: {result.Error.Message}";
    }

    public override PageResult OnNext()
    {
        SharedState.Set("DatabaseServer", _databaseServer);
        SharedState.Set("DatabaseName", _databaseName);
        SharedState.Set("IntegratedSecurity", _integratedSecurity);
        SharedState.Set("DbUserName", _userName);
        using var pw = GetPassword("DbPassword");
        if (!pw.IsEmpty)
            SharedState.SetSensitive("DbPassword", pw.Span);
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
