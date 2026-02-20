using System.Collections.ObjectModel;
using FalkForge.Plugins.Sql;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class DatabaseServerPage : MasPageBase<DatabaseServerView>
{
    private bool _useExisting = true;
    private string _databaseServer = @".\SQLEXPRESS";
    private string _databaseName = "MultiAccess";
    private bool _isSearching;

    public override string Title => "Choose database server";
    public override string? Subtitle => "Select database for MultiAccess";

    public bool IsSearching
    {
        get => _isSearching;
        set => SetField(ref _isSearching, value);
    }

    public ObservableCollection<string> AvailableServers { get; } = [];
    public ObservableCollection<string> AvailableDatabases { get; } = [];

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

    public async Task SearchServersAsync()
    {
        var discovery = PluginServices.GetService<ISqlServerDiscovery>();
        if (discovery is null) return;

        IsSearching = true;
        try
        {
            var result = await discovery.DiscoverServersAsync();
            if (result.IsSuccess)
            {
                AvailableServers.Clear();
                foreach (var server in result.Value)
                    AvailableServers.Add(server);
            }
        }
        finally
        {
            IsSearching = false;
        }
    }

    public async Task LoadDatabasesAsync()
    {
        if (string.IsNullOrWhiteSpace(DatabaseServer)) return;

        var lister = PluginServices.GetService<IDatabaseLister>();
        if (lister is null) return;

        var result = await lister.ListDatabasesAsync(DatabaseServer, true);
        if (result.IsSuccess)
        {
            AvailableDatabases.Clear();
            foreach (var db in result.Value)
                AvailableDatabases.Add(db);
        }
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
