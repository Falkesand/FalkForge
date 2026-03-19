using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// A bundle with related bundle detection and dependency management.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Simple Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D"))
        .UpgradeCode(new Guid("B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6E"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI(themeColor: "#0078D4")
        // Detect a related bundle (e.g. a previous version using a different upgrade code)
        .RelatedBundle("C3D4E5F6-A7B8-4C9D-0E1F-2A3B4C5D6E7F")
        // Declare this bundle as a dependency provider (other bundles can depend on it)
        .DependencyProvider("Demo.SimpleBundle", "1.0.0", "Simple Bundle")
        .Chain(chain => chain
            .MsiPackage("MyApp.msi", p => p
                .Id("MyApp")
                .DisplayName("My Application")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});