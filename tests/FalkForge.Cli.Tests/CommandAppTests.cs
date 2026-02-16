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
