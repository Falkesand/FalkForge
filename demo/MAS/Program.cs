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
    .Localization(loc => loc
        .DefaultCulture("en-US")
        .AddJsonResource<WelcomePage>("lang.strings.en-US.json")
        .AddJsonResource<WelcomePage>("lang.strings.sv-SE.json")
        .DetectCulture()
        .AllowLanguageSelection())
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()
        .Add<DatabaseServerPage>()
        .Add<ConfirmParametersPage>()
        .Add<InstallProgressPage>()
        .Add<CompletionPage>()
        .Add<AdvancedInstallDirMultiServerPage>()
        .Add<AdvancedInstallDirMultiServerExPage>()
        .Add<DatabaseConnectionSettingsPage>()
        .Add<MultiServerAdvancedSettingsPage>()
        .Add<MultiServerExAdvancedSettingsPage>()));