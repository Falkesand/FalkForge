using FalkForge.Cli;
using FalkForge.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("forge");

    // forge --version reports the single-source version from the root Directory.Build.props.
    // Set explicitly so the reported version is the FalkForge.Cli assembly's informational
    // version rather than whatever entry assembly happens to host the CommandApp.
    config.SetApplicationVersion(VersionInfo.CliVersion);

    config.Settings.Registrar.Register<IConsoleOutput, SpectreConsoleOutput>();

    config.AddCommand<InitCommand>("init")
        .WithDescription("Scaffold a starter installer project (csproj + fluent Program.cs + payload)")
        .WithExample("init")
        .WithExample("init", "-o", "./my-installer", "--name", "My App")
        .WithExample("init", "--type", "bundle", "--name", "My Suite")
        .WithExample("init", "--from-publish", "./bin/Release/net10.0/publish");

    config.AddCommand<BuildCommand>("build")
        .WithDescription("Compile an installer definition (.cs or .json) into MSI/Bundle")
        .WithExample("build", "installer.cs")
        .WithExample("build", "installer.cs", "-o", "./output")
        .WithExample("build", "installer.cs", "-c", "Debug", "--verbose")
        .WithExample("build", "installer.json");

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Run validators on an installer definition (.cs or .json) without producing output")
        .WithExample("validate", "installer.cs")
        .WithExample("validate", "installer.cs", "--verbose")
        .WithExample("validate", "installer.json");

    config.AddCommand<PlanCommand>("plan")
        .WithDescription("Run the installer pipeline through planning and output the plan without installing")
        .WithExample("plan", "installer.exe")
        .WithExample("plan", "installer.exe", "-o", "plan.json")
        .WithExample("plan", "installer.exe", "--json");

    config.AddCommand<PlanDiffCommand>("plan-diff")
        .WithDescription("Diff two installer artifacts (MSI or EXE bundle) and report what changed")
        .WithExample("plan-diff", "v1.msi", "v2.msi")
        .WithExample("plan-diff", "v1.exe", "v2.exe", "--markdown")
        .WithExample("plan-diff", "v1.msi", "v2.msi", "--json");

    config.AddCommand<VerifyCommand>("verify")
        .WithDescription("Independently verify a shipped artifact by rebuilding from source and byte-comparing")
        .WithExample("verify", "app.msi", "--rebuild", "installer.csproj")
        .WithExample("verify", "installer.exe", "--rebuild", "installer.csproj", "--json")
        .WithExample("verify", "app.msi", "--rebuild", "installer.csproj", "--source-date-epoch", "1577836800");

    config.AddCommand<InspectCommand>("inspect")
        .WithDescription("Display MSI metadata (tables, features, summary info)")
        .WithExample("inspect", "package.msi")
        .WithExample("inspect", "package.msi", "--verbose");

    config.AddCommand<DecompileCommand>("decompile")
        .WithDescription("Decompile an MSI or bundle EXE into C# source code")
        .WithExample("decompile", "package.msi")
        .WithExample("decompile", "package.msi", "-o", "installer.cs")
        .WithExample("decompile", "bundle.exe")
        .WithExample("decompile", "bundle.exe", "-o", "installer.cs");

    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("Migrate an existing installer (.msi, .msm, or .exe) to a buildable FalkForge C# project")
        .WithExample("migrate", "package.msi", "--falkforge-src", "../FalkForge/src")
        .WithExample("migrate", "bundle.exe", "-o", "./migrated", "--falkforge-src", "../FalkForge/src")
        .WithExample("migrate", "legacy.exe", "--falkforge-src", "../FalkForge/src");

    config.AddCommand<ExtractCommand>("extract")
        .WithDescription("Extract files from an MSI or payloads from an EXE bundle")
        .WithExample("extract", "package.msi", "-o", "./output")
        .WithExample("extract", "bundle.exe", "--list")
        .WithExample("extract", "bundle.exe", "-o", "./output")
        .WithExample("extract", "bundle.exe", "-o", "./output", "--package", "ServerMsi");

    config.AddCommand<WinGetCommand>("winget")
        .WithDescription("Generate WinGet manifest files from an existing MSI")
        .WithExample("winget", "package.msi", "--id", "Contoso.MyApp", "--license", "MIT", "--desc", "A tool")
        .WithExample("winget", "package.msi", "--id", "Contoso.MyApp", "--license", "MIT", "--desc", "A tool", "--url", "https://example.com/app.msi", "-o", "./manifests");

    config.AddBranch("rules", rules =>
    {
        rules.SetDescription("Inspect and query the validation rule catalog");
        rules.AddCommand<RulesListCommand>("list")
            .WithDescription("List validation rules for a given target model type")
            .WithExample("rules", "list")
            .WithExample("rules", "list", "--target", "patch")
            .WithExample("rules", "list", "--section", "Service")
            .WithExample("rules", "list", "--severity", "error")
            .WithExample("rules", "list", "--json");
        rules.AddCommand<RulesExplainCommand>("explain")
            .WithDescription("Print full metadata for a single validation rule")
            .WithExample("rules", "explain", "PKG001")
            .WithExample("rules", "explain", "SVC003");
    });

    config.AddBranch("loc", loc =>
    {
        loc.SetDescription("Localization tooling");
        loc.AddCommand<LocExportCommand>("export")
            .WithDescription("Export built-in localization JSON as an override starting point")
            .WithExample("loc", "export")
            .WithExample("loc", "export", "--culture", "en-US", "-o", "./loc")
            .WithExample("loc", "export", "--culture", "en-US", "-o", "custom-en-US.json")
            .WithExample("loc", "export", "--list");
    });

    config.AddBranch("bundle", bundle =>
    {
        bundle.SetDescription("Bundle signing operations (detach/reattach for code signing)");
        bundle.AddCommand<BundleDetachCommand>("detach")
            .WithDescription("Detach PE stub from bundle for external signing")
            .WithExample("bundle", "detach", "installer.exe", "--stub", "stub.exe", "--data", "bundle.dat");
        bundle.AddCommand<BundleReattachCommand>("reattach")
            .WithDescription("Reattach signed PE stub to bundle data")
            .WithExample("bundle", "reattach", "--stub", "signed.exe", "--data", "bundle.dat", "-o", "installer.exe");
    });
});

return await app.RunAsync(args).ConfigureAwait(false);
