using System.Text;
using FalkForge.Plugins.Sql;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class DatabaseConnectionSettingsPage : MasPageBase<DatabaseConnectionSettingsView>
{
    private string _databaseName = "MultiAccess";
    private string _databaseServer = @".\SQLEXPRESS";
    private bool _integratedSecurity = true;
    private bool _skipTest;
    private string _testResult = string.Empty;
    private string _userName = "AUSR_AptusWeb";

    public override string Title => Localize("DbConnectionSettings.Title");
    public override string? Subtitle => Localize("DbConnectionSettings.Subtitle");

    public string GroupHeader => Localize("DbConnectionSettings.GroupHeader");
    public string ServerLabel => Localize("DbConnectionSettings.ServerLabel");
    public string DatabaseNameLabel => Localize("DbConnectionSettings.DatabaseNameLabel");
    public string IntegratedSecurityCheckbox => Localize("DbConnectionSettings.IntegratedSecurityCheckbox");
    public string UserNameLabel => Localize("DbConnectionSettings.UserNameLabel");
    public string PasswordLabel => Localize("DbConnectionSettings.PasswordLabel");
    public string ShowButtonText => Localize("DbConnectionSettings.ShowButton");
    public string ShowPasswordTooltip => Localize("DbConnectionSettings.ShowPasswordTooltip");
    public string TestConnectionButtonText => Localize("DbConnectionSettings.TestConnectionButton");
    public string SkipTestCheckbox => Localize("DbConnectionSettings.SkipTestCheckbox");

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

    public string WarningText => Localize("DbConnectionSettings.WarningText");

    public async Task TestConnectionAsync()
    {
        var tester = PluginServices.GetService<IConnectionTester>();
        if (tester is null) return;

        TestResult = Localize("DbConnectionSettings.TestResultTesting");
        using var pw = GetPassword("DbPassword");
        var passwordStr = pw.IsEmpty ? string.Empty : Encoding.UTF8.GetString(pw.Span);
        var result = await tester.TestConnectionAsync(
            DatabaseServer, DatabaseName, IntegratedSecurity, UserName, passwordStr);
        TestResult = result.IsSuccess
            ? Localize("DbConnectionSettings.TestResultSuccess")
            : string.Format(Localize("DbConnectionSettings.TestResultFailed"), result.Error.Message);
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
    {
        return PageResult.GoTo<AdvancedInstallDirMultiServerExPage>();
    }

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