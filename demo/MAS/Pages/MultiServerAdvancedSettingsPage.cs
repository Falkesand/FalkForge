using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class MultiServerAdvancedSettingsPage : MasPageBase<MultiServerAdvancedSettingsView>
{
    private string _dsnName = "MultiAccess";
    private string _serviceAccount = "LocalSystem";
    private string _servicePassword = string.Empty;
    private string _dsnWarning = string.Empty;

    public override string Title => "MultiServer Advanced Settings";

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

    public string ServicePassword
    {
        get => _servicePassword;
        set => SetField(ref _servicePassword, value);
    }

    public string ServiceWarning => "If the account name is changed the new account must be of type service account to have permission to start MultiServer as a service.";

    public string IntegratedSecurityNote => "If integrated security is used make sure that the service account have correct permissions to the database.";

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerDsnName", _dsnName);
        SharedState.Set("MultiServerServiceAccount", _serviceAccount);
        SharedState.Set("MultiServerServicePassword", _servicePassword);
        SharedState.Set("MultiServerInstallAsService", true);
        return PageResult.GoTo<MultiServerExAdvancedSettingsPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<DatabaseConnectionSettingsPage>();
}
