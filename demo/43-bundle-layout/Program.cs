using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Group payloads into named containers for offline layout scenarios.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Layout Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("55667788-99AA-4BB0-CCDD-EEFF00112233"))
        .UpgradeCode(new Guid("66778899-AABB-4CC0-DDEE-FF0011223344"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .Chain(chain => chain
            .MsiPackage("Core.msi", p => p
                .Id("Core")
                .DisplayName("Core Components")
                .Vital(true)
                .Container("CoreContainer"))
            .MsiPackage("Extras.msi", p => p
                .Id("Extras")
                .DisplayName("Extra Components")
                .Vital(false)
                .Container("ExtrasContainer")))
        .Container("CoreContainer")
        .Container("ExtrasContainer")
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});