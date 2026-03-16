using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// ---------------------------------------------------------------------------
// MultiAccess MSI -- Main application package
// WiX parity: setup/MultiAccess/*.wxs
// UpgradeCode matches WiX: A796F128-60A6-4009-8F63-C8ECB0CC26F5
// ---------------------------------------------------------------------------

var installFolder = KnownFolder.ProgramFiles / "Aptus" / "MultiAccess";

return Installer.Build(args, package =>
{
    // --- Product Information ---
    package.Name = "MultiAccess";
    package.Manufacturer = "ASSA ABLOY Opening Solutions Sweden AB";
    package.Version = new Version(8, 9, 0);
    package.UpgradeCode = new Guid("A796F128-60A6-4009-8F63-C8ECB0CC26F5");
    package.Scope = InstallScope.PerMachine;
    package.DefaultInstallDirectory = installFolder;
    package.UseDialogSet(MsiDialogSet.None);

    // --- Major Upgrade ---
    package.MajorUpgrade(mu =>
        mu.Schedule(RemoveExistingProductsSchedule.AfterInstallValidate));

    // --- Launch Condition (downgrade prevention) ---
    package.Require(
        Condition.IsInstalled | Condition.Raw("NOT NEWER_VERSION_FOUND"),
        "A newer version of [ProductName] is already installed. Exiting installation.");

    // --- Properties ---
    package.Property("INSTALLDB", "false", p => p.IsSecure = true);
    package.Property("ATTACHDATABASE", "false", p => p.IsSecure = true);

    // --- Directory Structure ---
    // ProgramFiles\Aptus\MultiAccess\
    //   SendFiles\       (NetworkService+Users: GenericAll)
    //   Documentation\
    //   Utilities\
    //   Scripts\
    //   Database\Backup\ (NetworkService: GenericAll)

    // --- Folder Permissions ---
    // SDDL: D:(A;OICI;GA;;;NS) = NetworkService GenericAll
    //        D:(A;OICI;GA;;;BU) = Users GenericAll

    // INSTALLFOLDER: Users GenericAll
    package.Permission("INSTALLFOLDER", p =>
    {
        p.ForTable("CreateFolder");
        p.Sddl = "D:(A;OICI;GA;;;BU)";
    });

    // SendFiles: NetworkService + Users GenericAll
    package.CreateFolder(cf => cf.Id("SendFiles").Directory("SENDFILEDIRECTORY"));
    package.Permission("SENDFILEDIRECTORY", p =>
    {
        p.ForTable("CreateFolder");
        p.Sddl = "D:(A;OICI;GA;;;NS)(A;OICI;GA;;;BU)";
    });

    // Database\Backup: NetworkService GenericAll
    package.CreateFolder(cf => cf.Id("DbBackup").Directory("DBBACKUP"));
    package.Permission("DBBACKUP", p =>
    {
        p.ForTable("CreateFolder");
        p.Sddl = "D:(A;OICI;GA;;;NS)";
    });

    // --- Features ---
    // Feature: MainApplication -- core exe + DLLs + shortcuts + utilities + scripts + docs
    package.Feature("MainApplication", f =>
    {
        f.Title = "MultiAccess";
        f.Files(files => files
            .Add("payload/MultiAccess.exe")
            .To(installFolder));
    });

    // Feature: Database -- .mdf/.ldf files (Permanent: survive uninstall)
    package.Feature("Database", f =>
    {
        f.Title = "Database";
        f.Files(files => files
            .Add("payload/MultiAccess.mdf")
            .Add("payload/MultiAccess.ldf")
            .Permanent()
            .To(installFolder / "Database"));
    });

    // Feature: MAConfig -- config file preserved across upgrades
    package.Feature("MAConfig", f =>
    {
        f.Title = "MultiAccessConfig";
        f.Files(files => files
            .Add("payload/MultiAccessStyra.exe.config")
            .NeverOverwrite()
            .Permanent()
            .To(installFolder));
    });

    // --- Registry: Install folder persistence ---
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine, @"SOFTWARE\Aptus\MultiAccessInstall", key =>
            key.Value("InstallFolder", MsiProperty.Custom("INSTALLFOLDER"))));

    // --- Registry: SQL Attach configuration ---
    // Stores all DB configuration values from UI properties into HKLM for later retrieval
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine, @"SOFTWARE\Aptus\SQLAttach", key => key
            .Value("INSTALLFOLDER", MsiProperty.Custom("INSTALLFOLDER"))
            .Value("INSTALLDB", MsiProperty.Custom("INSTALLDB"))
            .Value("ATTACHDATABASE", MsiProperty.Custom("ATTACHDATABASE"))
            .Value("DBFOLDER", MsiProperty.Custom("DBFOLDER"))
            .Value("DB_MDFLOCATION", MsiProperty.Custom("DB_MDFLOCATION"))
            .Value("DB_LDFLOCATION", MsiProperty.Custom("DB_LDFLOCATION"))
            .Value("DB_SERVER", MsiProperty.Custom("DB_SERVER"))
            .Value("DB_ATTACHINTEGRATEDSECURITY", MsiProperty.Custom("DB_ATTACHINTEGRATEDSECURITY"))
            .Value("DB_ATTACHUSER", MsiProperty.Custom("DB_ATTACHUSER"))
            .Value("DB_ATTACHPASSWORD", MsiProperty.Custom("DB_ATTACHPASSWORD"))
            .Value("DB_DATABASE", MsiProperty.Custom("DB_DATABASE"))
            .Value("SERVER_MDFLOCATION", MsiProperty.Custom("SERVER_MDFLOCATION"))
            .Value("SERVER_LDFLOCATION", MsiProperty.Custom("SERVER_LDFLOCATION"))
            .Value("DB_INTEGRATEDSECURITY", MsiProperty.Custom("DB_INTEGRATEDSECURITY"))
            .Value("DB_USER", MsiProperty.Custom("DB_USER"))
            .Value("DB_PASSWORD", MsiProperty.Custom("DB_PASSWORD"))));

    // --- Shortcuts ---
    // Start Menu: MultiAccess Styra -> MultiAccessStyra.exe
    package.Shortcut("MultiAccess Styra", "MultiAccess.exe")
        .WithDescription("MultiAccess Styra")
        .WithWorkingDirectory("[INSTALLFOLDER]")
        .OnStartMenu("MultiAccess Styra");

    // Desktop: MultiAccess Styra -> MultiAccessStyra.exe
    package.Shortcut("MultiAccess Styra", "MultiAccess.exe")
        .WithDescription("MultiAccess Styra")
        .WithWorkingDirectory("[INSTALLFOLDER]")
        .OnDesktop();

    // --- Registry: Shortcut keypaths ---
    // WiX requires registry keypaths for shortcut components
    package.Registry(reg => reg
        .Key(RegistryRoot.CurrentUser, @"Software\Aptus\MultiAccessStyra", key =>
            key.DWord("installed", 1)));

    package.Registry(reg => reg
        .Key(RegistryRoot.CurrentUser, @"Software\Aptus", key =>
            key.DWord("installed", 1)));

}, new MsiCompiler());
