using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// ---------------------------------------------------------------------------
// MultiServerEx MSI -- Extended service package
// No direct WiX equivalent -- mirrors MultiServer structure for the
// extended service variant. Uses a distinct UpgradeCode and install path.
// ---------------------------------------------------------------------------

var installFolder = KnownFolder.ProgramFiles / "Aptus" / "MultiServerEx";

return Installer.Build(args, package =>
{
    // --- Product Information ---
    package.Name = "MultiServerEx";
    package.Manufacturer = "ASSA ABLOY Opening Solutions Sweden AB";
    package.Version = new Version(8, 9, 0);
    package.UpgradeCode = new Guid("C3D4E5F6-A7B8-4C9D-0E1F-2A3B4C5D6E7F");
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
    package.Property("ARPNOMODIFY", "yes", p => p.IsSecure = true);
    package.Property("ASSERVICE", "true", p => p.IsSecure = true);

    // Database connection defaults
    package.Property("DB_SERVER", @".\SQLEXPRESS", p => p.IsSecure = true);
    package.Property("DB_DATABASE", "MultiServerEx", p => p.IsSecure = true);
    package.Property("DB_USER", "Multi", p => p.IsSecure = true);
    package.Property("DB_PASSWORD", "Access", p => p.IsSecure = true);
    package.Property("DB_INTEGRATEDSECURITY", "true", p => p.IsSecure = true);
    package.Property("DB_PORT", "", p => p.IsSecure = true);

    // Service configuration defaults
    package.Property("SERVICENAME", "MultiServerEx", p => p.IsSecure = true);
    package.Property("SERVICEPASSWORD", "", p => p.IsSecure = true);
    package.Property("SERVICEACCOUNT", "LocalSystem", p => p.IsSecure = true);
    package.Property("ODBCNAME", "MultiAccess", p => p.IsSecure = true);

    // --- Files ---
    package.Files(files => files
        .Add("payload/MultiServerEx.exe")
        .To(installFolder));

    // --- Service Installation ---
    // Installs as a Windows service when ASSERVICE ~= "true"
    package.Service("MultiServerEx", svc =>
    {
        svc.DisplayName = "[SERVICENAME]";
        svc.Executable = "MultiServerEx.exe";
        svc.Description = "MultiServerEx service";
        svc.StartMode = ServiceStartMode.Automatic;
        svc.Arguments = "DSN=[ODBCNAME]";
        svc.AccountProperty("[SERVICEACCOUNT]");
        svc.Password = "[SERVICEPASSWORD]";
        svc.Condition("ASSERVICE ~= \"true\"");
        svc.Permission(perm =>
        {
            perm.User = "Everyone";
            perm.Sddl = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWRPWPDTLOCRRC;;;SU)(A;;GA;;;BA)(A;;GA;;;WD)";
        });
    });

    // --- Service Control ---
    package.ServiceControl(sc => sc
        .ServiceName("[SERVICENAME]")
        .StartOnInstall()
        .StopOnInstall()
        .StopOnUninstall()
        .DeleteOnUninstall()
        .Wait(false));

    // --- Registry: Install folder persistence ---
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine, @"SOFTWARE\Aptus\MultiServerExInstall", key => key
            .Value("InstallFolder", MsiProperty.Custom("INSTALLFOLDER"))
            .Value("Location", MsiProperty.Custom("INSTALLFOLDER"))));

    // --- Registry: Service name persistence ---
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine, @"SOFTWARE\Aptus\MultiServerEx", key =>
            key.Value("servicename", MsiProperty.Custom("SERVICENAME"))));

    // --- Registry: Event Log source ---
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine,
            @"SYSTEM\CurrentControlSet\Services\Eventlog\Application\MultiServerEx", key => key
                .Value("EventMessageFile", @"[INSTALLFOLDER]\MultiServerEx.dll",
                    RegistryValueType.ExpandString)
                .DWord("TypesSupported", 7)));

}, new MsiCompiler());
