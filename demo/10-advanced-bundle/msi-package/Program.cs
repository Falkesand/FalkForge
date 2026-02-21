using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

// Demo 10 -- Advanced Bundle: MSI Package
//
// A simple MSI package used as a chain item by the parent bundle.
// Installs a single application executable with registry entries
// and a major upgrade strategy.

var payloadDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, p =>
{
    p.Name = "Northwind Application";
    p.Manufacturer = "Northwind Traders";
    p.Version = new Version(2, 5, 0);
    p.UpgradeCode = new Guid("D1E2F3A4-B5C6-4D7E-8F9A-0B1C2D3E4F5A");

    p.UseDialogSet(MsiDialogSet.Minimal);

    p.Localization(loc => loc
        .AddBuiltInCultures()
        .DefaultCulture("en-US")
        .DetectCulture());

    var installDir = KnownFolder.ProgramFiles / "Northwind Traders" / "NorthwindApp";
    p.DefaultInstallDirectory = installDir;

    p.Files(f => f
        .Add(Path.Combine(payloadDir, "app.exe"))
        .To(installDir));

    p.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\Northwind\NorthwindApp", k => k
            .Value("Version", "2.5.0")
            .Value("InstallPath", MsiProperty.InstallFolder)));

    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version of Northwind Application is already installed."));

    p.Require(Condition.IsWindows10OrLater, "Northwind Application requires Windows 10 or later.");

}, new MsiCompiler());
