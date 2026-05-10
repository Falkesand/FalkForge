using FalkForge.Cli;
using FalkForge.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("forge");

    config.Settings.Registrar.Register<IConsoleOutput, SpectreConsoleOutput>();

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

    // PlanCommand is intentionally not wired until plan-only engine mode is complete.

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

return app.Run(args);
