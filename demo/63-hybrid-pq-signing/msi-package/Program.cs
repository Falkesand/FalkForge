using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Demo 63 -- Hybrid Post-Quantum Signing: MSI Package
//
// A minimal MSI package used as a chain item by the parent bundle.
// Kept intentionally simple -- the focus of this demo is the hybrid
// (ECDSA-P256 + ML-DSA-65) manifest signing in the bundle project.

var payloadDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, p =>
{
    p.Name = "Hybrid PQ Signing Demo Application";
    p.Manufacturer = "FalkForge Demo";
    p.Version = new Version(1, 0, 0);
    p.UpgradeCode = new Guid("7D0762A5-998A-4E44-9A44-6DE357833D50");

    p.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "HybridPqSigningDemo";
    p.DefaultInstallDirectory = installDir;

    p.Files(f => f
        .Add(Path.Combine(payloadDir, "app.exe"))
        .To(installDir));

    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version is already installed."));
}, new MsiCompiler());
