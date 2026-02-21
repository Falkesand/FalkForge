using FalkForge.Plugins.Odbc;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class MultiServerExAdvancedSettingsPage : MasPageBase<MultiServerExAdvancedSettingsView>
{
    private string _dsnName = "MultiAccessx64";
    private string _serviceAccount = "LocalSystem";
    private string _dsnWarning = string.Empty;

    public override string Title => Localize("MSExAdvancedSettings.Title");

    public string OdbcGroupHeader => Localize("MSExAdvancedSettings.OdbcGroupHeader");
    public string DsnNameLabel => Localize("MSExAdvancedSettings.DsnNameLabel");
    public string CheckDsnButtonText => Localize("MSExAdvancedSettings.CheckDsnButton");
    public string OdbcAdminButtonText => Localize("MSExAdvancedSettings.OdbcAdminButton");
    public string ServiceGroupHeader => Localize("MSExAdvancedSettings.ServiceGroupHeader");
    public string ServiceNameLabel => Localize("MSExAdvancedSettings.ServiceNameLabel");
    public string ServiceAccountLabel => Localize("MSExAdvancedSettings.ServiceAccountLabel");
    public string PasswordLabel => Localize("MSExAdvancedSettings.PasswordLabel");

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

    public string ServiceName => "MultiServerEx";

    public string ServiceAccount
    {
        get => _serviceAccount;
        set => SetField(ref _serviceAccount, value);
    }

    public string ServiceWarning => Localize("MSExAdvancedSettings.ServiceWarning");

    public string IntegratedSecurityNote => Localize("MSExAdvancedSettings.IntegratedSecurityNote");

    public void CheckDsnName()
    {
        var odbc = PluginServices.GetService<IOdbcManager>();
        if (odbc is null) return;

        var result = odbc.DsnExists(DsnName);
        if (result.IsSuccess && result.Value)
            DsnWarning = string.Format(Localize("MSExAdvancedSettings.DsnWarningFormat"), DsnName);
        else
            DsnWarning = string.Empty;
    }

    public void LaunchOdbcAdmin()
    {
        var odbc = PluginServices.GetService<IOdbcManager>();
        odbc?.LaunchOdbcAdministrator();
    }

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerExDsnName", _dsnName);
        SharedState.Set("MultiServerExServiceAccount", _serviceAccount);
        using var pw = GetPassword("MultiServerExServicePassword");
        if (!pw.IsEmpty)
            SharedState.SetSensitive("MultiServerExServicePassword", pw.Span);
        SharedState.Set("MultiServerExInstallAsService", true);
        return PageResult.GoTo<ConfirmParametersPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<MultiServerAdvancedSettingsPage>();
}
