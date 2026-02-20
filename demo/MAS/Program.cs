using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()
        .Add<DatabaseServerPage>()
        .Add<ConfirmParametersPage>()));
