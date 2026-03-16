#pragma warning disable CA1822 // UI-bound properties must remain instance members

using FalkForge.Plugins.Odbc;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

/// <summary>
/// Advanced-only page for MultiServer ODBC DSN and Windows service configuration.
/// The DSN check validates whether the name already exists to avoid overwriting.
/// Matches the WiX BA ServiceView for the MultiServer package.
/// </summary>
public sealed class MultiServerAdvancedSettingsPage : MasPageBase<MultiServerAdvancedSettingsView>
{
    private bool _isChecking;
    private string _dsnName = "MultiAccess";
    private string _dsnWarning = string.Empty;
    private string _serviceAccount = "LocalSystem";
    private string _checkButtonContent = string.Empty;

    public override string Title => Localize("MSAdvancedSettings.Title");

    public string OdbcGroupHeader => Localize("MSAdvancedSettings.OdbcGroupHeader");
    public string DsnNameLabel => Localize("MSAdvancedSettings.DsnNameLabel");
    public string CheckButtonContent
    {
        get => string.IsNullOrEmpty(_checkButtonContent) ? Localize("MSAdvancedSettings.CheckDsnButton") : _checkButtonContent;
        private set => SetField(ref _checkButtonContent, value);
    }
    public string OdbcAdminButtonText => Localize("MSAdvancedSettings.OdbcAdminButton");
    public string ServiceGroupHeader => Localize("MSAdvancedSettings.ServiceGroupHeader");
    public string ServiceNameLabel => Localize("MSAdvancedSettings.ServiceNameLabel");
    public string ServiceAccountLabel => Localize("MSAdvancedSettings.ServiceAccountLabel");
    public string PasswordLabel => Localize("MSAdvancedSettings.PasswordLabel");
    public string ShowButtonText => Localize("Shell.ShowButton");
    public string ShowPasswordTooltip => Localize("Shell.ShowPasswordTooltip");

    public bool IsChecking
    {
        get => _isChecking;
        set => SetField(ref _isChecking, value);
    }

    public string DsnName
    {
        get => _dsnName;
        set
        {
            if (SetField(ref _dsnName, value))
                DsnWarning = string.Empty;
        }
    }

    public string DsnWarning
    {
        get => _dsnWarning;
        set => SetField(ref _dsnWarning, value);
    }

    public string ServiceName => "MultiServer";

    public string ServiceAccount
    {
        get => _serviceAccount;
        set => SetField(ref _serviceAccount, value);
    }

    public string ServiceWarning => Localize("MSAdvancedSettings.ServiceWarning");

    public string IntegratedSecurityNote => Localize("MSAdvancedSettings.IntegratedSecurityNote");

    public void CheckDsnName()
    {
        var odbc = PluginServices.GetService<IOdbcManager>();
        if (odbc is null) return;

        IsChecking = true;
        CheckButtonContent = Localize("MSAdvancedSettings.Checking");
        try
        {
            var result = odbc.DsnExists(DsnName);
            if (result.IsSuccess && result.Value)
            {
                DsnWarning = string.Format(Localize("MSAdvancedSettings.DsnWarningFormat"), DsnName);
                CheckButtonContent = Localize("MSAdvancedSettings.DsnExists");
            }
            else
            {
                DsnWarning = string.Empty;
                CheckButtonContent = Localize("MSAdvancedSettings.DsnAvailable");
            }
        }
        finally
        {
            IsChecking = false;
            _ = ResetCheckButtonAsync();
        }
    }

    private async Task ResetCheckButtonAsync()
    {
        await Task.Delay(2000);
        CheckButtonContent = string.Empty;
    }

    public void LaunchOdbcAdmin()
    {
        var odbc = PluginServices.GetService<IOdbcManager>();
        odbc?.LaunchOdbcAdministrator();
    }

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerDsnName", _dsnName);
        SharedState.Set("MultiServerServiceAccount", _serviceAccount);
        using var pw = GetPassword("MultiServerServicePassword");
        if (!pw.IsEmpty)
            SharedState.SetSensitive("MultiServerServicePassword", pw.Span);
        SharedState.Set("MultiServerServiceName", "MultiServer");
        SharedState.Set("MultiServerInstallAsService", true);
        return PageResult.GoTo<MultiServerExAdvancedSettingsPage>();
    }

    public override PageResult OnBack()
    {
        return PageResult.GoTo<DatabaseConnectionSettingsPage>();
    }
}