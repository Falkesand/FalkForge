using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Demo 62 -- Require-Signed Updates: MSI Package
//
// A minimal MSI package used as a chain item by the parent bundle.
// Kept intentionally simple -- the focus of this demo is the update-trust
// authoring config (Integrity + UpdateFeed + epoch/revoke) in the bundle project.

var payloadDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, p =>
{
    p.Name = "Require-Signed Updates Demo Application";
    p.Manufacturer = "FalkForge Demo";
    p.Version = new Version(1, 0, 0);
    p.UpgradeCode = new Guid("9139AA76-737C-4AC3-B7AF-480C4C0AAF4C");

    p.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "RequireSignedUpdatesDemo";
    p.DefaultInstallDirectory = installDir;

    p.Files(f => f
        .Add(Path.Combine(payloadDir, "app.exe"))
        .To(installDir));

    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version is already installed."));
}, new MsiCompiler());
