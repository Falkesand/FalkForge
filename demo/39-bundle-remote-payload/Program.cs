using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Download a package from a URL at install time instead of embedding it.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Remote Payload Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("C1C2C3C4-D5D6-4E7E-8F8F-9A9A0B0B1C1C"))
        .UpgradeCode(new Guid("D1D2D3D4-E5E6-4F7F-8A8A-9B9B0C0C1D1D"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .Chain(chain => chain
            .MsiPackage("MyApp.msi", p => p
                .Id("MyApp")
                .DisplayName("My Application")
                .Vital(true)
                .RemotePayload(
                    "https://releases.example.com/myapp/1.0.0/MyApp.msi",
                    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    10485760)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});
