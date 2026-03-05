using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Configure automatic update checking from a feed URL.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Auto-Update Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("33445566-7788-4990-AABB-CCDDEEFF0011"))
        .UpgradeCode(new Guid("44556677-8899-4AA0-BBCC-DDEEFF001122"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .UpdateFeed("https://updates.example.com/myapp/feed.json")
        .Chain(chain => chain
            .MsiPackage("MyApp.msi", p => p
                .Id("MyApp")
                .DisplayName("My Application")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});