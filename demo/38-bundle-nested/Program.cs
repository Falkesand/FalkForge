using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Nest a child bundle inside a parent bundle.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Parent Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("A1A2A3A4-B5B6-4C7C-8D8D-9E9E0F0F1A1A"))
        .UpgradeCode(new Guid("B1B2B3B4-C5C6-4D7D-8E8E-9F9F0A0A1B1B"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .Chain(chain => chain
            .BundlePackage("ChildSetup.exe", p => p
                .Id("ChildBundle")
                .DisplayName("Child Application Bundle")
                .Vital(true))
            .MsiPackage("ParentApp.msi", p => p
                .Id("ParentApp")
                .DisplayName("Parent Application")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});
