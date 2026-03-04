using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Demo 15 -- Bundle Signing: MSI Package
//
// A minimal MSI package used as a chain item by the parent bundle.
// Kept intentionally simple — the focus of this demo is the
// detach → sign → reattach workflow in the bundle project.

var payloadDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, p =>
{
    p.Name = "Signing Demo Application";
    p.Manufacturer = "FalkForge Demo";
    p.Version = new Version(1, 0, 0);
    p.UpgradeCode = new Guid("8A1B2C3D-4E5F-6A7B-8C9D-0E1F2A3B4C5D");

    p.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "SigningDemo";
    p.DefaultInstallDirectory = installDir;

    p.Files(f => f
        .Add(Path.Combine(payloadDir, "app.exe"))
        .To(installDir));

    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version is already installed."));

}, new MsiCompiler());
