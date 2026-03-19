using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Bundle a Windows Update (.msu) hotfix as a prerequisite.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("MSU Package Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("E5F6A7B8-C9D0-4E1F-2A3B-4C5D6E7F8A9B"))
        .UpgradeCode(new Guid("F6A7B8C9-D0E1-4F2A-3B4C-5D6E7F8A9B0C"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .Chain(chain => chain
            .MsuPackage("windows-hotfix-kb123456.msu", p => p
                .Id("KB123456")
                .DisplayName("Windows Hotfix KB123456"))
            .MsiPackage("MyApp.msi", p => p
                .Id("MyApp")
                .DisplayName("My Application")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});