using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Demo 60 -- Trusted Key Rotation: MSI Package
//
// A minimal MSI package used as a chain item by the parent bundle.
// Kept intentionally simple -- the focus of this demo is the dual-sign / key
// rotation workflow in the bundle project.

var payloadDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, p =>
{
    p.Name = "Key Rotation Demo Application";
    p.Manufacturer = "FalkForge Demo";
    p.Version = new Version(1, 0, 0);
    p.UpgradeCode = new Guid("51CAA378-B6E8-41D7-9354-F3A1B55CDC08");

    p.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "KeyRotationDemo";
    p.DefaultInstallDirectory = installDir;

    p.Files(f => f
        .Add(Path.Combine(payloadDir, "app.exe"))
        .To(installDir));

    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version is already installed."));
}, new MsiCompiler());
