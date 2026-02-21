using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

return Installer.Build(args, p =>
{
    // Package metadata
    p.Name = "Acme Client-Server Suite";
    p.Manufacturer = "Acme Corporation";
    p.Version = new Version(3, 5, 0);
    p.UpgradeCode = new Guid("B7A3E4D1-9F2C-4E8B-A1D6-3C5F7E9B2A4D");

    // FeatureTree UI -- user picks which features to install
    p.UseDialogSet(MsiDialogSet.FeatureTree);

    p.Localization(loc => loc
        .AddBuiltInCultures()
        .DefaultCulture("en-US")
        .DetectCulture());

    // Install directory
    p.DefaultInstallDirectory = KnownFolder.ProgramFiles / "Acme Corporation" / "ClientServer";

    // --- Feature: Client ---
    p.Feature("Client", f =>
    {
        f.Title = "Client Application";
        f.Description = "Desktop client with user interface";
        f.IsDefault = true;
        f.IsRequired = false;
    });

    // Client files
    p.Files(f => f
        .Add("payload/client/client.exe")
        .Add("payload/client/client.core.dll")
        .Add("payload/client/client.ui.dll")
        .To(KnownFolder.ProgramFiles / "Acme Corporation" / "ClientServer" / "Client"));

    // Client shortcuts
    p.Shortcut("Acme Client", "client.exe")
        .OnDesktop()
        .OnStartMenu("Acme");

    // Client registry
    p.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\Acme\ClientServer\Client", k => k
            .Value("Version", "3.5.0")));

    // --- Feature: Server ---
    p.Feature("Server", f =>
    {
        f.Title = "Server Application";
        f.Description = "Background service for data processing";
        f.IsDefault = true;
        f.IsRequired = false;
    });

    // Server files
    p.Files(f => f
        .Add("payload/server/server.exe")
        .Add("payload/server/server.core.dll")
        .Add("payload/server/server.data.dll")
        .Add("payload/server/appsettings.json")
        .To(KnownFolder.ProgramFiles / "Acme Corporation" / "ClientServer" / "Server"));

    // Server Windows service
    p.Service("AcmeServer", svc =>
    {
        svc.DisplayName = "Acme Server";
        svc.Description = "Acme Client-Server Suite background service";
        svc.Executable = "server.exe";
        svc.StartMode = ServiceStartMode.Automatic;
        svc.Account = ServiceAccount.LocalSystem;
    });

    // Server registry
    p.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\Acme\ClientServer\Server", k => k
            .Value("Version", "3.5.0")
            .Value("Port", "8080")
            .Value("InstallPath", MsiProperty.InstallFolder)));

    // Server environment variable
    p.EnvironmentVariable("ACME_SERVER_PORT", "8080", ev =>
    {
        ev.IsSystem = true;
        ev.Action = EnvironmentVariableAction.Set;
    });

    // Install directory environment variable (typed MsiProperty API)
    p.EnvironmentVariable("ACME_INSTALL_DIR", MsiProperty.InstallFolder, ev =>
    {
        ev.IsSystem = true;
        ev.Action = EnvironmentVariableAction.Set;
    });

    // --- Feature: Documentation ---
    p.Feature("Documentation", f =>
    {
        f.Title = "Documentation";
        f.Description = "User guide, admin guide, and API reference";
        f.IsDefault = true;
        f.IsRequired = false;
    });

    // Documentation files
    p.Files(f => f
        .Add("payload/docs/userguide.pdf")
        .Add("payload/docs/admin-guide.pdf")
        .Add("payload/docs/api-reference.html")
        .To(KnownFolder.ProgramFiles / "Acme Corporation" / "ClientServer" / "Docs"));

    // Major upgrade support
    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version is already installed."));

    // Launch condition: Require Windows 10+ (typed Condition API)
    p.Require(Condition.IsWindows10OrLater, "This application requires Windows 10 or later.");
});
