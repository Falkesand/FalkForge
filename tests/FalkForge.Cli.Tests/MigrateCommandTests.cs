using FalkForge.Cli.Commands;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class MigrateCommandTests : IDisposable
{
    private readonly string _tempDir;

    public MigrateCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"falk-migrate-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "migrate", null);

    private string BuildRealBundle()
    {
        var payloadPath = Path.Combine(_tempDir, "MyApp.msi");
        File.WriteAllBytes(payloadPath, [0x01, 0x02, 0x03, 0x04, 0x05]);

        var model = new BundleModel
        {
            Name = "TestMigBundle",
            Manufacturer = "Test Corp",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = "MyApp",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "MyApp",
                    SourcePath = payloadPath,
                }
            ],
            Chain =
            [
                new PackageChainItem(new BundlePackageModel
                {
                    Id = "MyApp",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "MyApp",
                    SourcePath = payloadPath,
                })
            ]
        };

        var outDir = Path.Combine(_tempDir, "bundle-out");
        Directory.CreateDirectory(outDir);
        var result = new BundleCompiler().Compile(model, outDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return result.Value;
    }

    [Fact]
    public void Execute_NonExistentFile_ReturnsValidationFailure()
    {
        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console);
        var settings = new Settings.MigrateSettings { FilePath = "nonexistent_xyz.exe" };

        var result = command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.ValidationFailure, result);
    }

    [Fact]
    public void Execute_NonExistentFile_WritesErrorToConsole()
    {
        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console);
        var settings = new Settings.MigrateSettings { FilePath = "nonexistent_xyz.exe" };

        command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Contains(console.Errors, e => e.Contains("File not found"));
    }

    [Fact]
    public void Execute_MsiOnNonWindows_ReturnsRuntimeError()
    {
        if (OperatingSystem.IsWindows())
            return; // guard: Windows CAN do MSI

        var fakeMsi = Path.Combine(_tempDir, "legacy.msi");
        File.WriteAllBytes(fakeMsi, [0x00]);

        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console);
        var settings = new Settings.MigrateSettings { FilePath = fakeMsi, FalkForgeSourcePath = "../../src" };

        var result = command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, result);
        Assert.Contains(console.Errors, e => e.Contains("Windows"));
    }

    [Fact]
    public void Execute_FalkForgeSrcFlowsIntoGeneratedCsproj()
    {
        var bundlePath = BuildRealBundle();
        var outDir = Path.Combine(_tempDir, "migrate-out");
        const string fakeSrc = "../../src";

        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console);
        var settings = new Settings.MigrateSettings
        {
            FilePath = bundlePath,
            OutputPath = outDir,
            FalkForgeSourcePath = fakeSrc
        };

        var result = command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, result);
        var csprojFiles = Directory.GetFiles(outDir, "*.csproj", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(csprojFiles);
        var csprojContent = File.ReadAllText(csprojFiles[0]);
        Assert.Contains(fakeSrc, csprojContent);
    }

    [Fact]
    public void Execute_WritesTextFilesAndPayloadsToDisk()
    {
        var bundlePath = BuildRealBundle();
        var outDir = Path.Combine(_tempDir, "migrate-out2");

        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console);
        var settings = new Settings.MigrateSettings
        {
            FilePath = bundlePath,
            OutputPath = outDir,
            FalkForgeSourcePath = "../../src"
        };

        var result = command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, result);
        Assert.True(File.Exists(Path.Combine(outDir, "Program.cs")));
        Assert.True(File.Exists(Path.Combine(outDir, "MIGRATION-REPORT.md")));
        var payloadFiles = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories)
            .Where(f => f.Contains("payload"))
            .ToArray();
        Assert.NotEmpty(payloadFiles);
    }

    [Fact]
    public void Execute_PrintsSummaryToConsole()
    {
        var bundlePath = BuildRealBundle();
        var outDir = Path.Combine(_tempDir, "migrate-out3");

        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console);
        var settings = new Settings.MigrateSettings
        {
            FilePath = bundlePath,
            OutputPath = outDir,
            FalkForgeSourcePath = "../../src"
        };

        command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Contains(console.AllOutput, line => line.Contains("Migration complete") || line.Contains(outDir));
    }
}
