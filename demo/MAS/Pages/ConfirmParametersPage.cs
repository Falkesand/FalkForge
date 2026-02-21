using System.Collections.ObjectModel;
using FalkForge.Ui.Abstractions;
using MAS.Models;
using MAS.Views;

namespace MAS.Pages;

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

        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupInstallationType"),
            Entries =
            [
                new("Concatenate", "Install"),
                new("Konfigurera", "Install"),
                new("MultiAccess", "Install"),
                new("MultiServer", "Install"),
                new("MultiServerEx", "Install"),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupInstallFolderMA"),
            Entries = [new(Localize("ConfirmParameters.InstallFolder"), @"C:\Program Files (x86)\Aptus\MultiAccess")]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupDatabaseServer"),
            Entries =
            [
                new(Localize("ConfirmParameters.CreateDbConnection"), yes),
                new(Localize("ConfirmParameters.CreateEmptyDb"), useExisting ? no : yes),
                new(Localize("ConfirmParameters.DatabaseName"), dbName),
                new(Localize("ConfirmParameters.ServerPath"), dbServer),
                new(Localize("ConfirmParameters.UseExistingDb"), useExisting ? yes : no),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupInstallFolderMS"),
            Entries = [new(Localize("ConfirmParameters.InstallFolder"), @"C:\Program Files (x86)\Aptus\MultiServer")]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupDbConnectionSettings"),
            Entries =
            [
                new(Localize("ConfirmParameters.NameOfDatabase"), dbName),
                new(Localize("ConfirmParameters.DatabaseServer"), dbServer),
                new(Localize("ConfirmParameters.IntegratedSecurity"), yes),
                new(Localize("ConfirmParameters.UserName"), ""),
                new(Localize("ConfirmParameters.Password"), ""),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupMSAdvancedSettings"),
            Entries =
            [
                new(Localize("ConfirmParameters.DsnName"), dbName),
                new(Localize("ConfirmParameters.InstallAsService"), no),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupInstallFolderMSEx"),
            Entries = [new(Localize("ConfirmParameters.InstallFolder"), @"C:\Program Files (x86)\Aptus\MultiServerEx")]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = Localize("ConfirmParameters.GroupMSExAdvancedSettings"),
            Entries = [new(Localize("ConfirmParameters.InstallAsService"), no)]
        });

        if (installType == "Advanced")
        {
            var msInstallFolder = SharedState.Get<string>("MultiServerInstallFolder")
                ?? @"C:\Program Files (x86)\Aptus\MultiServer";
            var msExInstallFolder = SharedState.Get<string>("MultiServerExInstallFolder")
                ?? @"C:\Program Files (x86)\Aptus\MultiServerEx";
            var intSecurity = SharedState.Get<bool>("IntegratedSecurity");
            var dbUser = SharedState.Get<string>("DbUserName") ?? "";
            var msDsn = SharedState.Get<string>("MultiServerDsnName") ?? "MultiAccess";
            var msExDsn = SharedState.Get<string>("MultiServerExDsnName") ?? "MultiAccessx64";
            var msServiceAccount = SharedState.Get<string>("MultiServerServiceAccount") ?? "LocalSystem";
            var msExServiceAccount = SharedState.Get<string>("MultiServerExServiceAccount") ?? "LocalSystem";

            ParameterGroups[3] = new ParameterGroup
            {
                Header = Localize("ConfirmParameters.GroupInstallFolderMS"),
                Entries = [new(Localize("ConfirmParameters.InstallFolder"), msInstallFolder)]
            };

            ParameterGroups[4] = new ParameterGroup
            {
                Header = Localize("ConfirmParameters.GroupDbConnectionSettings"),
                Entries =
                [
                    new(Localize("ConfirmParameters.NameOfDatabase"), dbName),
                    new(Localize("ConfirmParameters.DatabaseServer"), dbServer),
                    new(Localize("ConfirmParameters.IntegratedSecurity"), intSecurity ? yes : no),
                    new(Localize("ConfirmParameters.UserName"), dbUser),
                    new(Localize("ConfirmParameters.Password"), ""),
                ]
            };

            ParameterGroups[5] = new ParameterGroup
            {
                Header = Localize("ConfirmParameters.GroupMSAdvancedSettings"),
                Entries =
                [
                    new(Localize("ConfirmParameters.DsnName"), msDsn),
                    new(Localize("ConfirmParameters.InstallAsService"), yes),
                    new(Localize("ConfirmParameters.ServiceName"), "MultiServer"),
                    new(Localize("ConfirmParameters.ServiceAccount"), msServiceAccount),
                    new(Localize("ConfirmParameters.Password"), ""),
                ]
            };

            ParameterGroups[6] = new ParameterGroup
            {
                Header = Localize("ConfirmParameters.GroupInstallFolderMSEx"),
                Entries = [new(Localize("ConfirmParameters.InstallFolder"), msExInstallFolder)]
            };

            ParameterGroups[7] = new ParameterGroup
            {
                Header = Localize("ConfirmParameters.GroupMSExAdvancedSettings"),
                Entries =
                [
                    new(Localize("ConfirmParameters.DsnName"), msExDsn),
                    new(Localize("ConfirmParameters.InstallAsService"), yes),
                    new(Localize("ConfirmParameters.ServiceName"), "MultiServerEx"),
                    new(Localize("ConfirmParameters.ServiceAccount"), msExServiceAccount),
                    new(Localize("ConfirmParameters.Password"), ""),
                ]
            };

            ParameterGroups.Add(new ParameterGroup
            {
                Header = Localize("ConfirmParameters.GroupPackages"),
                Entries =
                [
                    new("Concatenate", "Install"),
                    new("Konfigurera", "Install"),
                    new("MultiAccess", "Install"),
                    new("MultiServer", "Install"),
                    new("MultiServerEx", "Install"),
                ]
            });
        }

        return Task.CompletedTask;
    }

    public override PageResult OnNext()
        => PageResult.Install;

    public override PageResult OnBack()
    {
        var installType = SharedState.Get<string>("InstallationType");
        return installType == "Advanced"
            ? PageResult.GoTo<MultiServerExAdvancedSettingsPage>()
            : PageResult.GoTo<DatabaseServerPage>();
    }
}
