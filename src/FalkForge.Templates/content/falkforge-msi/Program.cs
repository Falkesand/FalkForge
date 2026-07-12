using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Build the installer:
//   dotnet run              -> writes the MSI to the current directory
//   dotnet run -- -o out    -> writes the MSI to out\
return Installer.Build(args, package =>
{
    package.Name = "PRODUCT-NAME";
    package.Manufacturer = "My Company";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Files(files => files
        .FromDirectory("payload")
        .To(KnownFolder.ProgramFiles / "My Company" / "PRODUCT-NAME"));

    // Start Menu shortcut. TargetFile is a payload file name (not a full path — the
    // compiler resolves it against the installed folder above); update it to match your
    // application's real executable once you replace payload/ with your app.
    package.Shortcut("PRODUCT-NAME", "PRODUCT-NAME.exe")
        .OnStartMenu();
}, new MsiCompiler());
