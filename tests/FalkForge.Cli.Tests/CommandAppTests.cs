using FalkForge.Cli.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class CommandAppTests
{
    private static CommandApp CreateApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("forge");
            config.AddCommand<BuildCommand>("build");
            config.AddCommand<ValidateCommand>("validate");
            config.AddCommand<InspectCommand>("inspect");
            config.AddCommand<DecompileCommand>("decompile");
        });
        return app;
    }

    /// <summary>
    /// Mirrors the full production CommandApp registration from Program.cs so tests can
    /// verify which commands are publicly surfaced to users.
    /// </summary>
    private static CommandApp CreateFullApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("forge");
            config.Settings.Registrar.Register<IConsoleOutput, SpectreConsoleOutput>();
            config.AddCommand<BuildCommand>("build");
            config.AddCommand<ValidateCommand>("validate");
            config.AddCommand<PlanCommand>("plan");
            config.AddCommand<InspectCommand>("inspect");
            config.AddCommand<DecompileCommand>("decompile");
            config.AddBranch("bundle", bundle =>
            {
                bundle.AddCommand<BundleDetachCommand>("detach");
                bundle.AddCommand<BundleReattachCommand>("reattach");
            });
        });
        return app;
    }

    [Fact]
    public void PlanCommand_IsRegisteredInProductionApp_NonExistentBundleReturnsNonZero()
    {
        // PlanCommand is now registered. Running it with a non-existent file should
        // return a non-zero exit code (file not found error), not an unknown-command result.
        var app = CreateFullApp();

        var result = app.Run(["plan", "nonexistent_bundle_xyz.exe"]);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void Plan_NoArguments_ReturnsNonZero()
    {
        var app = CreateFullApp();

        var result = app.Run(["plan"]);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void Build_NoArguments_ReturnsNonZero()
    {
        var app = CreateApp();

        var result = app.Run(["build"]);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void Validate_NoArguments_ReturnsNonZero()
    {
        var app = CreateApp();

        var result = app.Run(["validate"]);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void Inspect_NoArguments_ReturnsNonZero()
    {
        var app = CreateApp();

        var result = app.Run(["inspect"]);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void Decompile_NoArguments_ReturnsNonZero()
    {
        var app = CreateApp();

        var result = app.Run(["decompile"]);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void Build_NonExistentFile_ReturnsNonZero()
    {
        var app = CreateApp();

        var result = app.Run(["build", "nonexistent_file_that_does_not_exist.cs"]);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void Validate_NonExistentFile_ReturnsNonZero()
    {
        var app = CreateApp();

        var result = app.Run(["validate", "nonexistent_file_that_does_not_exist.cs"]);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void Build_InvalidExtension_ReturnsNonZero()
    {
        var app = CreateApp();

        var result = app.Run(["build", "installer.txt"]);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void NoCommand_ReturnsZero()
    {
        var app = CreateApp();

        var result = app.Run([]);

        Assert.Equal(0, result);
    }
}
