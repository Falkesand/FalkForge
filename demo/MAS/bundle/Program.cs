using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

var packagesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "packages"));

string MsiPath(string name) => Path.Combine(packagesDir, name, "bin", "Release", $"{name}-8.9.0.msi");

return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("MultiAccess Suite")
        .Manufacturer("ASSA ABLOY")
        .Version("8.9.0")
        .BundleId(new Guid("10203040-5060-4070-8090-A0B0C0D0E0F0"))
        .UpgradeCode(new Guid("F0E0D0C0-B0A0-4090-8070-605040302010"))
        .Scope(InstallScope.PerMachine)
        .UseCustomUI("../MAS.csproj")
        .Chain(chain => chain
            .MsiPackage(MsiPath("MultiAccess"), p => p
                .Id("MultiAccess")
                .DisplayName("MultiAccess")
                .Version("8.9.0")
                .Vital(true))
            .MsiPackage(MsiPath("MultiServer"), p => p
                .Id("MultiServer")
                .DisplayName("MultiServer")
                .Version("8.9.0")
                .Vital(true))
            .MsiPackage(MsiPath("MultiServerEx"), p => p
                .Id("MultiServerEx")
                .DisplayName("MultiServerEx")
                .Version("8.9.0")
                .Vital(true))
            .MsiPackage(MsiPath("Konfigurera"), p => p
                .Id("Konfigurera")
                .DisplayName("Konfigurera")
                .Version("8.9.0")
                .Vital(true))
            .MsiPackage(MsiPath("Concatenate"), p => p
                .Id("Concatenate")
                .DisplayName("Concatenate")
                .Version("8.9.0")
                .Vital(true)))
        .Build();

    var compiler = new BundleCompiler();

    // Use pre-published NativeAOT engine binary as the bootstrapper stub
    var enginePath = Environment.GetEnvironmentVariable("FALKFORGE_ENGINE_PATH");
    if (enginePath is not null)
        compiler.EngineStubPath = enginePath;

    return compiler.Compile(bundle, outputPath);
});
