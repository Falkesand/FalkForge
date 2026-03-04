using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Use rollback boundaries to isolate package failures.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Rollback Boundaries Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("11223344-5566-4778-899A-AABBCCDDEEFF"))
        .UpgradeCode(new Guid("22334455-6677-4889-9AAB-BBCCDDEEFF00"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .Chain(chain => chain
            // If Prerequisites fail, only prerequisites roll back
            .RollbackBoundary("Prerequisites")
            .MsiPackage("Runtime.msi", p => p
                .Id("Runtime")
                .DisplayName("Runtime Prerequisites")
                .Vital(true))
            // If Application fails, only application rolls back
            .RollbackBoundary("Application")
            .MsiPackage("MyApp.msi", p => p
                .Id("MyApp")
                .DisplayName("My Application")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});
