using FalkForge.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("forge");

    config.AddCommand<BuildCommand>("build")
        .WithDescription("Compile a C# installer definition into MSI/Bundle")
        .WithExample("build", "installer.cs")
        .WithExample("build", "installer.cs", "-o", "./output")
        .WithExample("build", "installer.cs", "-c", "Debug", "--verbose");

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Run validators on a C# installer definition without producing output")
        .WithExample("validate", "installer.cs")
        .WithExample("validate", "installer.cs", "--verbose");

    config.AddCommand<InspectCommand>("inspect")
        .WithDescription("Display MSI metadata (tables, features, summary info)")
        .WithExample("inspect", "package.msi")
        .WithExample("inspect", "package.msi", "--verbose");

    config.AddCommand<DecompileCommand>("decompile")
        .WithDescription("Decompile an MSI into C# source code")
        .WithExample("decompile", "package.msi")
        .WithExample("decompile", "package.msi", "-o", "installer.cs");
});

return app.Run(args);
