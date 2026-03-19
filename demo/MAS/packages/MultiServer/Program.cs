using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// ---------------------------------------------------------------------------
// MultiServer MSI -- Windows service package
// WiX parity: setup/MultiServer/MSMsi/*.wxs
// UpgradeCode matches WiX: c02ba94d-c03e-4509-bc8c-5f342c2b92fd
// ---------------------------------------------------------------------------

var installFolder = KnownFolder.ProgramFiles / "Aptus" / "MultiServer";

return Installer.Build(args, package =>
{
    // --- Product Information ---
    package.Name = "MultiServer";
    package.Manufacturer = "ASSA ABLOY Opening Solutions Sweden AB";
    package.Version = new Version(8, 9, 0);
    package.UpgradeCode = new Guid("c02ba94d-c03e-4509-bc8c-5f342c2b92fd");
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
    // ARPNOMODIFY hides Modify button in Add/Remove Programs
    package.Property("ARPNOMODIFY", "yes", p => p.IsSecure = true);
    package.Property("ASSERVICE", "true", p => p.IsSecure = true);

    // Database connection defaults
    package.Property("DB_SERVER", @".\SQLEXPRESS", p => p.IsSecure = true);
    package.Property("DB_DATABASE", "MultiServer", p => p.IsSecure = true);
    package.Property("DB_USER", "Multi", p => p.IsSecure = true);
    package.Property("DB_PASSWORD", "Access", p => p.IsSecure = true);
    package.Property("DB_INTEGRATEDSECURITY", "true", p => p.IsSecure = true);
    package.Property("DB_PORT", "", p => p.IsSecure = true);

    // Service configuration defaults
    package.Property("SERVICENAME", "MultiServer", p => p.IsSecure = true);
    package.Property("SERVICEPASSWORD", "", p => p.IsSecure = true);
    package.Property("SERVICEACCOUNT", "LocalSystem", p => p.IsSecure = true);
    package.Property("ODBCNAME", "MultiAccess", p => p.IsSecure = true);

    // --- Files ---
    // Standalone executable (always installed)
    package.Files(files => files
        .Add("payload/MultiServer.exe")
        .To(installFolder));

    // --- Service Installation ---
    // Installs as a Windows service when ASSERVICE ~= "true"
    package.Service("MultiServer", svc =>
    {
        svc.DisplayName = "[SERVICENAME]";
        svc.Executable = "MultiServer.exe";
        svc.Description = "MultiServer service";
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
    // Start on install, stop on install+uninstall, remove on uninstall
    package.ServiceControl(sc => sc
        .ServiceName("[SERVICENAME]")
        .StartOnInstall()
        .StopOnInstall()
        .StopOnUninstall()
        .DeleteOnUninstall()
        .Wait(false));

    // --- Registry: Install folder persistence ---
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine, @"SOFTWARE\Aptus\MultiServerInstall", key => key
            .Value("InstallFolder", MsiProperty.Custom("INSTALLFOLDER"))
            .Value("Location", MsiProperty.Custom("INSTALLFOLDER"))));

    // --- Registry: Service name persistence ---
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine, @"SOFTWARE\Aptus\MultiServer", key =>
            key.Value("servicename", MsiProperty.Custom("SERVICENAME"))));

    // --- Registry: Event Log source ---
    // Registers MultiServer as an event log source under Application log
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine,
            @"SYSTEM\CurrentControlSet\Services\Eventlog\Application\MultiServer", key => key
                .Value("EventMessageFile", @"[INSTALLFOLDER]\MultiServer.dll",
                    RegistryValueType.ExpandString)
                .DWord("TypesSupported", 7)));

}, new MsiCompiler());
