using FalkForge.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("forge");

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
});

return app.Run(args);
