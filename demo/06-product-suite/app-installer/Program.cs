using FalkForge;
using FalkForge.Models;

var payloadDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, p =>
{
    // ──────────────────────────────────────────────────────────────────
    // Package metadata
    // ──────────────────────────────────────────────────────────────────
    p.Name = "Acme Application";
    p.Manufacturer = "Acme Corporation";
    p.Version = new Version(2, 0, 0);
    p.UpgradeCode = new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D");
    p.LicenseFile = Path.Combine(payloadDir, "license.rtf");

    // InstallDir UI -- user picks install directory
    p.UseDialogSet(MsiDialogSet.InstallDir);

    // Install directory
    var installDir = KnownFolder.ProgramFiles / "Acme Corporation" / "AcmeApp";
    p.DefaultInstallDirectory = installDir;

    // ──────────────────────────────────────────────────────────────────
    // Feature: Application
    // ──────────────────────────────────────────────────────────────────
    p.Feature("Application", f =>
    {
        f.Title = "Acme Application";
        f.Description = "Core application files";
        f.IsDefault = true;
        f.IsRequired = true;
    });

    // Application files
    p.Files(f => f
        .Add(Path.Combine(payloadDir, "acmeapp.exe"))
        .Add(Path.Combine(payloadDir, "acmeapp.core.dll"))
        .Add(Path.Combine(payloadDir, "acmeapp.ui.dll"))
        .Add(Path.Combine(payloadDir, "config.json"))
        .To(installDir));

    // ──────────────────────────────────────────────────────────────────
    // Shortcuts
    // ──────────────────────────────────────────────────────────────────
    p.Shortcut("Acme Application", "acmeapp.exe")
        .WithDescription("Launch Acme Application")
        .OnDesktop();

    p.Shortcut("Acme Application", "acmeapp.exe")
        .WithDescription("Launch Acme Application")
        .OnStartMenu("Acme Corporation");

    // ──────────────────────────────────────────────────────────────────
    // Registry entries
    // ──────────────────────────────────────────────────────────────────
    p.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\Acme\AcmeApp", k => k
            .Value("Version", "2.0.0")
            .Value("InstallPath", MsiProperty.InstallFolder)));

    // ──────────────────────────────────────────────────────────────────
    // Major upgrade
    // ──────────────────────────────────────────────────────────────────
    p.MajorUpgrade(mu => mu
        .DowngradeErrorMessage("A newer version of Acme Application is already installed."));

    // Launch condition: Require Windows 10+
    p.Require(Condition.IsWindows10OrLater, "Acme Application requires Windows 10 or later.");
});
