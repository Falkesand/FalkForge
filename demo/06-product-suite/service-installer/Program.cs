using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

var payloadDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, p =>
{
    // ──────────────────────────────────────────────────────────────────
    // Package metadata
    // ──────────────────────────────────────────────────────────────────
    p.Name = "Acme Background Service";
    p.Manufacturer = "Acme Corporation";
    p.Version = new Version(2, 0, 0);
    p.UpgradeCode = new Guid("D4C3B2A1-6F5E-4B7A-9D8C-1F0E2A3B4C5D");
    p.Scope = InstallScope.PerMachine;

    // Minimal UI -- services don't need interactive directory selection
    p.UseDialogSet(MsiDialogSet.Minimal);

    p.Localization(loc => loc
        .AddBuiltInCultures()
        .DefaultCulture("en-US")
        .DetectCulture());

    // Install directory
    var installDir = KnownFolder.ProgramFiles / "Acme Corporation" / "AcmeService";
    p.DefaultInstallDirectory = installDir;

    // ──────────────────────────────────────────────────────────────────
    // Feature: Service
    // ──────────────────────────────────────────────────────────────────
    p.Feature("Service", f =>
    {
        f.Title = "Acme Background Service";
        f.Description = "Background service for data processing and synchronization";
        f.IsDefault = true;
        f.IsRequired = true;
    });

    // Service files
    p.Files(f => f
        .Add(Path.Combine(payloadDir, "acmeservice.exe"))
        .Add(Path.Combine(payloadDir, "acmeservice.core.dll"))
        .Add(Path.Combine(payloadDir, "appsettings.json"))
        .To(installDir));

    // ──────────────────────────────────────────────────────────────────
    // Windows Service definition
    // ──────────────────────────────────────────────────────────────────
    p.Service("AcmeService", svc =>
    {
        svc.DisplayName = "Acme Background Service";
        svc.Description = "Acme data processing and synchronization service";
        svc.Executable = "acmeservice.exe";
        svc.StartMode = ServiceStartMode.Automatic;
        svc.Account = ServiceAccount.LocalService;
    });

    // ──────────────────────────────────────────────────────────────────
    // Environment variable
    // ──────────────────────────────────────────────────────────────────
    p.EnvironmentVariable("ACME_SERVICE_PORT", "8080", ev =>
    {
        ev.IsSystem = true;
        ev.Action = EnvironmentVariableAction.Set;
    });

    // ──────────────────────────────────────────────────────────────────
    // Major upgrade
    // ──────────────────────────────────────────────────────────────────
    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version of Acme Background Service is already installed."));
});