using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class ExtractCommandTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "extract", null);

    [Fact]
    public void Validate_NoOutput_NoList_ReturnsError()
    {
        var settings = new ExtractSettings { FilePath = "test.msi" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("Output directory", result.Message);
    }

    [Fact]
    public void Validate_ListOnly_NoOutputRequired()
    {
        var settings = new ExtractSettings { FilePath = "bundle.exe", ListOnly = true };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_PackageOnMsi_ReturnsError()
    {
        var settings = new ExtractSettings
        {
            FilePath = "test.msi",
            OutputPath = "out",
            Packages = ["MyPackage"]
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--package", result.Message);
    }

    [Fact]
    public void Validate_ValidMsiWithOutput_Succeeds()
    {
        var settings = new ExtractSettings { FilePath = "test.msi", OutputPath = "out" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_ValidExeWithOutput_Succeeds()
    {
        var settings = new ExtractSettings { FilePath = "bundle.exe", OutputPath = "out" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_InvalidExtension_ReturnsError()
    {
        var settings = new ExtractSettings { FilePath = "readme.txt", OutputPath = "out" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains(".msi", result.Message);
    }

    [Fact]
    public void Execute_FileNotFound_ReturnsRuntimeError()
    {
        var console = new TestConsoleOutput();
        var command = new ExtractCommand(console);
        var settings = new ExtractSettings
        {
            FilePath = "nonexistent_file_xyz.msi",
            OutputPath = "out"
        };

        var result = command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, result);
    }

    [Fact]
    public void Execute_FileNotFound_WritesErrorMessage()
    {
        var console = new TestConsoleOutput();
        var command = new ExtractCommand(console);
        var settings = new ExtractSettings
        {
            FilePath = "nonexistent_file_xyz.msi",
            OutputPath = "out"
        };

        command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Contains(console.Errors, e => e.Contains("File not found"));
    }

    [Fact]
    public void Validate_ValidMsmWithOutput_Succeeds()
    {
        var settings = new ExtractSettings { FilePath = "merge.msm", OutputPath = "out" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_EmptyFilePath_ReturnsError()
    {
        var settings = new ExtractSettings { FilePath = "", OutputPath = "out" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("required", result.Message);
    }

    [Fact]
    public void Validate_PackageOnExe_Succeeds()
    {
        var settings = new ExtractSettings
        {
            FilePath = "bundle.exe",
            OutputPath = "out",
            Packages = ["MyPackage"]
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }
}
