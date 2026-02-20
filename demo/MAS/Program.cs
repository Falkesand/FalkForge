using FalkForge.Plugins.FileSystem;
using FalkForge.Plugins.Odbc;
using FalkForge.Plugins.Sql;
using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Plugin<SqlPlugin>()
    .Plugin<OdbcPlugin>()
    .Plugin<FileSystemPlugin>()
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()
        .Add<DatabaseServerPage>()
        .Add<ConfirmParametersPage>()
        .Add<AdvancedInstallDirMultiServerPage>()
        .Add<AdvancedInstallDirMultiServerExPage>()
        .Add<DatabaseConnectionSettingsPage>()
        .Add<MultiServerAdvancedSettingsPage>()
        .Add<MultiServerExAdvancedSettingsPage>()));
