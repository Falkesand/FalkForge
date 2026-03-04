using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Define variables with visibility controls and conditional package installation.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Variables Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("E1E2E3E4-F5F6-4A7A-8B8B-9C9C0D0D1E1E"))
        .UpgradeCode(new Guid("F1F2F3F4-A5A6-4B7B-8C8C-9D9D0E0E1F1F"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .Variable("InstallOptionalTools", v => v
            .Numeric()
            .Default("0"))
        // Persisted variable — survives bundle repair/modify sessions
        .Variable("InstallPath", v => v
            .String()
            .Default(@"C:\Program Files\Demo")
            .Persisted())
        // Hidden variable — excluded from install logs
        .Variable("LicenseKey", v => v
            .String()
            .Hidden())
        // Secret variable — excluded from logs AND persisted state (implies Hidden)
        .Variable("DatabasePassword", v => v
            .String()
            .Secret())
        .Chain(chain => chain
            .MsiPackage("CoreApp.msi", p => p
                .Id("CoreApp")
                .DisplayName("Core Application")
                .Vital(true))
            .MsiPackage("OptionalTools.msi", p => p
                .Id("OptionalTools")
                .DisplayName("Optional Developer Tools")
                .Vital(false)
                .InstallCondition("InstallOptionalTools = 1")))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});