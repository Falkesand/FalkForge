using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Demo 59 -- Bundle Integrity Signing: MSI Package
//
// A minimal MSI package used as a chain item by the parent bundle.
// Kept intentionally simple -- the focus of this demo is the bundle-level
// ECDSA integrity signing in the bundle project.

var payloadDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, p =>
{
    p.Name = "Integrity Signing Demo Application";
    p.Manufacturer = "FalkForge Demo";
    p.Version = new Version(1, 0, 0);
    p.UpgradeCode = new Guid("142A6BF1-B0F3-4ED4-B938-910C6BA51F59");

    p.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "IntegritySigningDemo";
    p.DefaultInstallDirectory = installDir;

    p.Files(f => f
        .Add(Path.Combine(payloadDir, "app.exe"))
        .To(installDir));

    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version is already installed."));
}, new MsiCompiler());
