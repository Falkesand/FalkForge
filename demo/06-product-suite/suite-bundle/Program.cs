using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// With FalkForge SDK source generation, use: ProjectOutputs.AppInstaller
// For standalone demo, reference built MSI paths directly:
const string appMsiPath = "../app-installer/app-installer.msi";
const string serviceMsiPath = "../service-installer/service-installer.msi";

var licenseFile = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "app-installer", "payload", "license.rtf"));

return Installer.BuildBundle(args, outputPath =>
{
    // ──────────────────────────────────────────────────────────────────
    // Build the bundle model
    // ──────────────────────────────────────────────────────────────────
    var bundle = new BundleBuilder()
        .Name("Acme Product Suite")
        .Manufacturer("Acme Corporation")
        .Version("2.0.0")
        .BundleId(new Guid("E1F2A3B4-C5D6-4E7F-8A9B-0C1D2E3F4A5B"))
        .UpgradeCode(new Guid("F4A5B6C7-D8E9-4F0A-1B2C-3D4E5F6A7B8C"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI(licenseFile, themeColor: "#2563EB")
        .Chain(chain => chain
            // Prerequisites boundary -- if the app MSI fails, roll back only the app
            .RollbackBoundary("Prerequisites")
            .MsiPackage(appMsiPath, p => p
                .Id("AcmeApp")
                .DisplayName("Acme Application")
                .Vital(true))
            // Services boundary -- if the service MSI fails, roll back only the service
            .RollbackBoundary("Services")
            .MsiPackage(serviceMsiPath, p => p
                .Id("AcmeService")
                .DisplayName("Acme Background Service")
                .Vital(true)))
        .Build();

    // ──────────────────────────────────────────────────────────────────
    // Compile the bundle to an EXE bootstrapper
    // ──────────────────────────────────────────────────────────────────
    var compiler = new BundleCompiler();
    return compiler.Compile(bundle, outputPath);
});