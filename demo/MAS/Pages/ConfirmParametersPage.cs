using System.Collections.ObjectModel;
using FalkForge.Ui.Abstractions;
using MAS.Models;
using MAS.Views;

namespace MAS.Pages;

/// <summary>
/// Read-only summary of all installation parameters before the user clicks Install.
/// Dynamically builds parameter groups based on Standard vs Advanced installation type.
/// Matches the WiX BA ConfirmParametersView.
/// </summary>
public sealed class ConfirmParametersPage : MasPageBase<ConfirmParametersView>
{
    public override string Title => Localize("ConfirmParameters.Title");
    public override string NextButtonText => Localize("Shell.InstallButton");

    public string NameColumn => Localize("ConfirmParameters.NameColumn");
    public string ValueColumn => Localize("ConfirmParameters.ValueColumn");

    public ObservableCollection<ParameterGroup> ParameterGroups { get; } = [];

    public override Task OnNavigatedToAsync()
    {
        ParameterGroups.Clear();

        var installType = SharedState.Get<string>("InstallationType") ?? "Standard";
        var useExisting = SharedState.Get<bool>("UseExistingDatabase");
        var dbServer = SharedState.Get<string>("DatabaseServer") ?? @".\SQLEXPRESS";
        var dbName = SharedState.Get<string>("DatabaseName") ?? "MultiAccess";
        var yes = Localize("ConfirmParameters.Yes");
        var no = Localize("ConfirmParameters.No");

        // Default paths used by both Standard and as fallback for Advanced
        const string defaultMaFolder = @"C:\Program Files (x86)\Aptus\MultiAccess";
        const string defaultMsFolder = @"C:\Program Files (x86)\Aptus\MultiServer";
        const string defaultMsExFolder = @"C:\Program Files (x86)\Aptus\MultiServerEx";

        // Read advanced values (will have user-chosen values or fall back to defaults)
        var msInstallFolder = SharedState.Get<string>("MultiServerInstallFolder") ?? defaultMsFolder;
        var msExInstallFolder = SharedState.Get<string>("MultiServerExInstallFolder") ?? defaultMsExFolder;
        var intSecurity = installType == "Advanced" ? SharedState.Get<bool>("IntegratedSecurity") : true;
        var dbUser = SharedState.Get<string>("DbUserName") ?? "";
        var msDsn = SharedState.Get<string>("MultiServerDsnName") ?? "MultiAccess";
        var msExDsn = SharedState.Get<string>("MultiServerExDsnName") ?? "MultiAccessx64";
        var msServiceAccount = SharedState.Get<string>("MultiServerServiceAccount") ?? "LocalSystem";
        var msExServiceAccount = SharedState.Get<string>("MultiServerExServiceAccount") ?? "LocalSystem";
        var msAsService = installType == "Advanced" && SharedState.Get<bool>("MultiServerInstallAsService");
        var msExAsService = installType == "Advanced" && SharedState.Get<bool>("MultiServerExInstallAsService");

        // --- Packages ---
        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupInstallationType"),
            Entries =
            [
                new ParameterEntry("Concatenate", "Install"),
                new ParameterEntry("Konfigurera", "Install"),
                new ParameterEntry("MultiAccess", "Install"),
                new ParameterEntry("MultiServer", "Install"),
                new ParameterEntry("MultiServerEx", "Install")
            ]
        });

        // --- MultiAccess install folder (always default in this demo) ---
        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupInstallFolderMA"),
            Entries = [new ParameterEntry(Localize("ConfirmParameters.InstallFolder"), defaultMaFolder)]
        });

        // --- Database server ---
        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupDatabaseServer"),
            Entries =
            [
                new ParameterEntry(Localize("ConfirmParameters.CreateDbConnection"), yes),
                new ParameterEntry(Localize("ConfirmParameters.CreateEmptyDb"), useExisting ? no : yes),
                new ParameterEntry(Localize("ConfirmParameters.DatabaseName"), dbName),
                new ParameterEntry(Localize("ConfirmParameters.ServerPath"), dbServer),
                new ParameterEntry(Localize("ConfirmParameters.UseExistingDb"), useExisting ? yes : no)
            ]
        });

        // --- MultiServer install folder ---
        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupInstallFolderMS"),
            Entries = [new ParameterEntry(Localize("ConfirmParameters.InstallFolder"), msInstallFolder)]
        });

        // --- Database connection settings ---
        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupDbConnectionSettings"),
            Entries =
            [
                new ParameterEntry(Localize("ConfirmParameters.NameOfDatabase"), dbName),
                new ParameterEntry(Localize("ConfirmParameters.DatabaseServer"), dbServer),
                new ParameterEntry(Localize("ConfirmParameters.IntegratedSecurity"), intSecurity ? yes : no),
                new ParameterEntry(Localize("ConfirmParameters.UserName"), dbUser),
                new ParameterEntry(Localize("ConfirmParameters.Password"), "")
            ]
        });

        // --- MultiServer advanced settings ---
        var msEntries = new List<ParameterEntry>
        {
            new(Localize("ConfirmParameters.DsnName"), msDsn),
            new(Localize("ConfirmParameters.InstallAsService"), msAsService ? yes : no)
        };
        if (msAsService)
        {
            msEntries.Add(new ParameterEntry(Localize("ConfirmParameters.ServiceName"), "MultiServer"));
            msEntries.Add(new ParameterEntry(Localize("ConfirmParameters.ServiceAccount"), msServiceAccount));
            msEntries.Add(new ParameterEntry(Localize("ConfirmParameters.Password"), ""));
        }
        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupMSAdvancedSettings"),
            Entries = msEntries
        });

        // --- MultiServerEx install folder ---
        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupInstallFolderMSEx"),
            Entries = [new ParameterEntry(Localize("ConfirmParameters.InstallFolder"), msExInstallFolder)]
        });

        // --- MultiServerEx advanced settings ---
        var msExEntries = new List<ParameterEntry>
        {
            new(Localize("ConfirmParameters.DsnName"), msExDsn),
            new(Localize("ConfirmParameters.InstallAsService"), msExAsService ? yes : no)
        };
        if (msExAsService)
        {
            msExEntries.Add(new ParameterEntry(Localize("ConfirmParameters.ServiceName"), "MultiServerEx"));
            msExEntries.Add(new ParameterEntry(Localize("ConfirmParameters.ServiceAccount"), msExServiceAccount));
            msExEntries.Add(new ParameterEntry(Localize("ConfirmParameters.Password"), ""));
        }
        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupMSExAdvancedSettings"),
            Entries = msExEntries
        });

        return Task.CompletedTask;
    }

    public override PageResult OnNext()
    {
        return PageResult.GoTo<InstallProgressPage>();
    }

    public override PageResult OnBack()
    {
        var installType = SharedState.Get<string>("InstallationType");
        return installType == "Advanced"
            ? PageResult.GoTo<MultiServerExAdvancedSettingsPage>()
            : PageResult.GoTo<DatabaseServerPage>();
    }
}