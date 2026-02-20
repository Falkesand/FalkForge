using System.Collections.ObjectModel;
using FalkForge.Ui.Abstractions;
using MAS.Models;
using MAS.Views;

namespace MAS.Pages;

public sealed class ConfirmParametersPage : MasPageBase<ConfirmParametersView>
{
    public override string Title => "Confirm Parameters";
    public override string NextButtonText => "Install";

    public ObservableCollection<ParameterGroup> ParameterGroups { get; } = [];

    public override Task OnNavigatedToAsync()
    {
        ParameterGroups.Clear();

        var installType = SharedState.Get<string>("InstallationType") ?? "Standard";
        var useExisting = SharedState.Get<bool>("UseExistingDatabase");
        var dbServer = SharedState.Get<string>("DatabaseServer") ?? @".\SQLEXPRESS";
        var dbName = SharedState.Get<string>("DatabaseName") ?? "MultiAccess";

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Select installation type",
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
            Header = "Installation folder for MultiAccess",
            Entries = [new("Install folder", @"C:\Program Files (x86)\Aptus\MultiAccess")]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Choose database server",
            Entries =
            [
                new("Create database connection", "Yes"),
                new("Create empty database", useExisting ? "No" : "Yes"),
                new("Database Name", dbName),
                new("Server path", dbServer),
                new("Use existing database", useExisting ? "Yes" : "No"),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Installation folder for MultiServer",
            Entries = [new("Install folder", @"C:\Program Files (x86)\Aptus\MultiServer")]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Database Connection Settings",
            Entries =
            [
                new("Name of database:", dbName),
                new("Database server:", dbServer),
                new("Integrated security:", "Yes"),
                new("User name:", ""),
                new("Password:", ""),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "MultiServer Advanced Settings",
            Entries =
            [
                new("DSN Name", dbName),
                new("Install as service", "No"),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Installation folder for MultiServerEx",
            Entries = [new("Install folder", @"C:\Program Files (x86)\Aptus\MultiServerEx")]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "MultiServerEx Advanced Settings",
            Entries = [new("Install as service", "No")]
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
                Header = "Installation folder for MultiServer",
                Entries = [new("Install folder", msInstallFolder)]
            };

            ParameterGroups[4] = new ParameterGroup
            {
                Header = "Database Connection Settings",
                Entries =
                [
                    new("Name of database:", dbName),
                    new("Database server:", dbServer),
                    new("Integrated security:", intSecurity ? "Yes" : "No"),
                    new("User name:", dbUser),
                    new("Password:", ""),
                ]
            };

            ParameterGroups[5] = new ParameterGroup
            {
                Header = "MultiServer Advanced Settings",
                Entries =
                [
                    new("DSN Name", msDsn),
                    new("Install as service", "Yes"),
                    new("Service name", "MultiServer"),
                    new("Service Account", msServiceAccount),
                    new("Password", ""),
                ]
            };

            ParameterGroups[6] = new ParameterGroup
            {
                Header = "Installation folder for MultiServerEx",
                Entries = [new("Install folder", msExInstallFolder)]
            };

            ParameterGroups[7] = new ParameterGroup
            {
                Header = "MultiServerEx Advanced Settings",
                Entries =
                [
                    new("DSN Name", msExDsn),
                    new("Install as service", "Yes"),
                    new("Service name", "MultiServerEx"),
                    new("Service Account", msExServiceAccount),
                    new("Password", ""),
                ]
            };

            ParameterGroups.Add(new ParameterGroup
            {
                Header = "Packages",
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
