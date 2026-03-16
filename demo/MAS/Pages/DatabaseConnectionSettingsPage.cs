using System.Text;
using FalkForge.Plugins.Sql;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

/// <summary>
/// Advanced-only page for configuring database connection credentials.
/// Supports integrated security or SQL authentication with a test-connection button.
/// Matches the WiX BA DatabaseConnectionSettingsView + AttachDataBaseSettingsView combined.
/// </summary>
public sealed class DatabaseConnectionSettingsPage : MasPageBase<DatabaseConnectionSettingsView>
{
    private string _databaseName = "MultiAccess";
    private string _databaseServer = @".\SQLEXPRESS";
    private bool _integratedSecurity = true;
    private bool _trustServerCertificate = true;
    private bool _skipTest;
    private bool _isTesting;
    private string _testResult = string.Empty;
    private string _userName = "AUSR_AptusWeb";
    private string _testButtonContent = string.Empty;

    public override string Title => Localize("DbConnectionSettings.Title");
    public override string? Subtitle => Localize("DbConnectionSettings.Subtitle");

    // --- Localized labels ---

    public string GroupHeader => Localize("DbConnectionSettings.GroupHeader");
    public string ServerLabel => Localize("DbConnectionSettings.ServerLabel");
    public string DatabaseNameLabel => Localize("DbConnectionSettings.DatabaseNameLabel");
    public string IntegratedSecurityCheckbox => Localize("DbConnectionSettings.IntegratedSecurityCheckbox");
    public string TrustServerCertificateCheckbox => Localize("DbConnectionSettings.TrustServerCertificateCheckbox");
    public string UserNameLabel => Localize("DbConnectionSettings.UserNameLabel");
    public string PasswordLabel => Localize("DbConnectionSettings.PasswordLabel");
    public string ShowButtonText => Localize("DbConnectionSettings.ShowButton");
    public string ShowPasswordTooltip => Localize("DbConnectionSettings.ShowPasswordTooltip");
    public string SkipTestCheckbox => Localize("DbConnectionSettings.SkipTestCheckbox");

    // --- Editable properties ---

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

    public bool TrustServerCertificate
    {
        get => _trustServerCertificate;
        set => SetField(ref _trustServerCertificate, value);
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

    public bool IsTesting
    {
        get => _isTesting;
        set => SetField(ref _isTesting, value);
    }

    public string TestResult
    {
        get => _testResult;
        set => SetField(ref _testResult, value);
    }

    public string TestButtonContent
    {
        get => string.IsNullOrEmpty(_testButtonContent) ? Localize("DbConnectionSettings.TestConnectionButton") : _testButtonContent;
        private set => SetField(ref _testButtonContent, value);
    }

    public string WarningText => Localize("DbConnectionSettings.WarningText");

    public async Task TestConnectionAsync()
    {
        var tester = PluginServices.GetService<IConnectionTester>();
        if (tester is null) return;

        IsTesting = true;
        TestButtonContent = Localize("DbConnectionSettings.Testing");
        try
        {
            TestResult = string.Empty;
            using var pw = GetPassword("DbPassword");
            var passwordStr = pw.IsEmpty ? string.Empty : Encoding.UTF8.GetString(pw.Span);
            var result = await tester.TestConnectionAsync(
                DatabaseServer, DatabaseName, IntegratedSecurity, UserName, passwordStr,
                TrustServerCertificate);
            if (result.IsSuccess)
            {
                TestButtonContent = Localize("DbConnectionSettings.ConnectionOk");
                TestResult = Localize("DbConnectionSettings.TestResultSuccess");
            }
            else
            {
                TestButtonContent = Localize("DbConnectionSettings.ConnectionFailed");
                TestResult = string.Format(Localize("DbConnectionSettings.TestResultFailed"), result.Error.Message);
            }
        }
        finally
        {
            IsTesting = false;
            _ = ResetTestButtonAsync();
        }
    }

    private async Task ResetTestButtonAsync()
    {
        await Task.Delay(2000);
        TestButtonContent = string.Empty;
    }

    public override PageResult OnNext()
    {
        SharedState.Set("DatabaseServer", _databaseServer);
        SharedState.Set("DatabaseName", _databaseName);
        SharedState.Set("IntegratedSecurity", _integratedSecurity);
        SharedState.Set("TrustServerCertificate", _trustServerCertificate);
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