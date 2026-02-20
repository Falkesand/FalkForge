using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class DatabaseServerPage : MasPageBase<DatabaseServerView>
{
    private bool _useExisting = true;
    private string _databaseServer = @".\SQLEXPRESS";
    private string _databaseName = "MultiAccess";

    public override string Title => "Choose database server";
    public override string? Subtitle => "Select database for MultiAccess";

    public bool UseExisting
    {
        get => _useExisting;
        set
        {
            if (SetField(ref _useExisting, value))
                OnPropertyChanged(nameof(CreateEmpty));
        }
    }

    public bool CreateEmpty
    {
        get => !_useExisting;
        set => UseExisting = !value;
    }

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

    public override PageResult OnNext()
    {
        SharedState.Set("UseExistingDatabase", _useExisting);
        SharedState.Set("DatabaseServer", _databaseServer);
        SharedState.Set("DatabaseName", _databaseName);

        var installType = SharedState.Get<string>("InstallationType");
        return installType == "Advanced"
            ? PageResult.GoTo<AdvancedInstallDirMultiServerPage>()
            : PageResult.GoTo<ConfirmParametersPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<InstallationTypePage>();
}
