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
}, new MsiCompiler());
