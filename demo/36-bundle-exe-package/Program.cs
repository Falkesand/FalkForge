using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Bundle an EXE prerequisite (e.g., Visual C++ Redistributable) with exit code mapping.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("EXE Package Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("C3D4E5F6-A7B8-4C9D-0E1F-2A3B4C5D6E7F"))
        .UpgradeCode(new Guid("D4E5F6A7-B8C9-4D0E-1F2A-3B4C5D6E7F8A"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .Chain(chain => chain
            .ExePackage("vcredist_x64.exe", p => p
                .Id("VCRedist")
                .DisplayName("Visual C++ Redistributable")
                .Vital(true)
                .ExitCode(0, ExitCodeBehavior.Success)
                .ExitCode(3010, ExitCodeBehavior.ScheduleReboot)
                .ExitCode(1638, ExitCodeBehavior.Success))
            .MsiPackage("MyApp.msi", p => p
                .Id("MyApp")
                .DisplayName("My Application")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});