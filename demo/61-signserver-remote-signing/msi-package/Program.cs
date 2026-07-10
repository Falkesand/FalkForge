using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Demo 61 -- SignServer Remote Signing: MSI Package
//
// A minimal MSI package used as a chain item by the parent bundle.
// Kept intentionally simple -- the focus of this demo is the remote-signing
// provider wiring in the bundle project.

var payloadDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, p =>
{
    p.Name = "SignServer Remote Signing Demo Application";
    p.Manufacturer = "FalkForge Demo";
    p.Version = new Version(1, 0, 0);
    p.UpgradeCode = new Guid("C6CCD504-4679-48B9-A0A6-886D9D9F9F25");

    p.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "SignServerDemo";
    p.DefaultInstallDirectory = installDir;

    p.Files(f => f
        .Add(Path.Combine(payloadDir, "app.exe"))
        .To(installDir));

    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version is already installed."));
}, new MsiCompiler());
