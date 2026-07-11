using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Build the installer:
//   dotnet run              -> writes the MSI and the self-extracting EXE bundle
//                              to the current directory
//   dotnet run -- -o out    -> writes both to out\
//
// The bundle's PE front is the real NativeAOT FalkForge engine, resolved automatically
// from the FalkForge.Engine.Runtime.win-x64 package that the FalkForge meta-package
// references (it lands in this project's build output under engine\).
return Installer.BuildBundle(args, outputPath =>
{
    // 1. Build the MSI the bundle chains.
    var package = new PackageBuilder();
    package.Name = "PRODUCT-NAME";
    package.Manufacturer = "My Company";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Files(files => files
        .FromDirectory("payload")
        .To(KnownFolder.ProgramFiles / "My Company" / "PRODUCT-NAME"));

    var msi = new MsiCompiler().Compile(package.Build(), outputPath);
    if (msi.IsFailure)
        return msi;

    // 2. Chain it into a self-extracting EXE bundle.
    var bundle = new BundleBuilder()
        .Name("PRODUCT-NAME")
        .Manufacturer("My Company")
        .Version("1.0.0")
        .BundleId(new Guid("0C55B22A-3B23-45A4-A3F1-6A1E2F0D5B01"))
        .UpgradeCode(new Guid("5D8E3A9C-7F14-4B06-9C2D-8E4A1B6F7C02"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI(themeColor: "#0078D4")
        .Chain(chain => chain
            .MsiPackage(msi.Value, p => p
                .Id("MainMsi")
                .DisplayName("PRODUCT-NAME")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});
