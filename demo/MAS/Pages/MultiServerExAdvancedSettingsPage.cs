using FalkForge.Plugins.Odbc;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class MultiServerExAdvancedSettingsPage : MasPageBase<MultiServerExAdvancedSettingsView>
{
    private string _dsnName = "MultiAccessx64";
    private string _serviceAccount = "LocalSystem";
    private string _dsnWarning = string.Empty;

    public override string Title => "MultiServerEx Advanced Settings";

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

    public string ServiceWarning => "If the account name is changed the new account must be of type service account to have permission to start MultiServer as a service.";

    public string IntegratedSecurityNote => "If integrated security is used make sure that the service account have correct permissions to the database.";

    public void CheckDsnName()
    {
        var odbc = PluginServices.GetService<IOdbcManager>();
        if (odbc is null) return;

        var result = odbc.DsnExists(DsnName);
        if (result.IsSuccess && result.Value)
            DsnWarning = $"DSN name, {DsnName}, already exists. Observe if using this name the installation will not overwrite existing settings.";
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
