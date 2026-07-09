using FalkForge.Cli.Commands;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Decompiler;
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

    // FIX D: file-not-found must align with DecompileCommand convention → RuntimeError.
    [Fact]
    public void Execute_NonExistentFile_ReturnsRuntimeError()
    {
        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console);
        var settings = new Settings.MigrateSettings { FilePath = "nonexistent_xyz.exe" };

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, result);
    }

    [Fact]
    public void Execute_NonExistentFile_WritesErrorToConsole()
    {
        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console);
        var settings = new Settings.MigrateSettings { FilePath = "nonexistent_xyz.exe" };

        command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

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

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

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

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

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

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

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

        command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.Contains(console.AllOutput, line => line.Contains("Migration complete") || line.Contains(outDir));
    }

    // ---------------------------------------------------------------------------
    // FIX A + B: path-traversal containment — hostile keys rejected, exit non-zero
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A TextFiles entry with a path-traversal key must be rejected and must NOT
    /// create a file outside the output directory.
    /// Passes a pre-built MigrationResult via the test-seam constructor overload.
    /// </summary>
    [Fact]
    public void Execute_HostileTextFileKey_IsRejectedAndNotWritten()
    {
        var bundlePath = BuildRealBundle();
        var outDir = Path.Combine(_tempDir, "migrate-hostile-txt");

        var hostileKey = ".." + Path.DirectorySeparatorChar + "evil.cs";
        var textFiles = new Dictionary<string, string>
        {
            ["Program.cs"] = "// safe",
            [hostileKey] = "// hostile"
        };
        var migration = new MigrationResult(textFiles, [], new Dictionary<string, byte[]>());

        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console, migration);
        var settings = new Settings.MigrateSettings
        {
            FilePath = bundlePath,
            OutputPath = outDir,
            FalkForgeSourcePath = "../../src"
        };

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        // FIX B: traversal detected → non-zero exit.
        Assert.Equal(ExitCodes.ValidationFailure, result);

        // FIX A: hostile file must NOT exist outside outDir.
        var escapedPath = Path.GetFullPath(Path.Combine(outDir, hostileKey));
        Assert.False(File.Exists(escapedPath), "Hostile text file must not be written outside output directory.");

        // FIX B: error line must name the offending key.
        Assert.Contains(console.Errors, e => e.Contains("evil.cs") || e.Contains(".."));

        // Safe file was still written.
        Assert.True(File.Exists(Path.Combine(outDir, "Program.cs")));
    }

    /// <summary>
    /// A Payloads entry with a path-traversal key must be rejected.
    /// Safe payload must still be written.
    /// </summary>
    [Fact]
    public void Execute_HostilePayloadKey_IsRejectedAndNotWritten()
    {
        var bundlePath = BuildRealBundle();
        var outDir = Path.Combine(_tempDir, "migrate-hostile-payload");

        var hostileKey = ".." + Path.DirectorySeparatorChar + "evil.bin";
        var payloads = new Dictionary<string, byte[]>
        {
            ["payload/safe.bin"] = [0x01, 0x02],
            [hostileKey] = [0xDE, 0xAD]
        };
        var migration = new MigrationResult(
            new Dictionary<string, string> { ["Program.cs"] = "// ok" },
            [],
            payloads);

        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console, migration);
        var settings = new Settings.MigrateSettings
        {
            FilePath = bundlePath,
            OutputPath = outDir,
            FalkForgeSourcePath = "../../src"
        };

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.ValidationFailure, result);

        var escapedPath = Path.GetFullPath(Path.Combine(outDir, hostileKey));
        Assert.False(File.Exists(escapedPath), "Hostile payload must not be written outside output directory.");

        Assert.Contains(console.Errors, e => e.Contains("evil.bin") || e.Contains(".."));

        // Safe payload still written.
        Assert.True(File.Exists(Path.Combine(outDir, "payload", "safe.bin")));
    }

    /// <summary>
    /// A normal nested payload key like <c>payload/sub/file.bin</c> must be accepted and written.
    /// </summary>
    [Fact]
    public void Execute_NestedPayloadKey_IsAcceptedAndWritten()
    {
        var bundlePath = BuildRealBundle();
        var outDir = Path.Combine(_tempDir, "migrate-nested");

        var payloads = new Dictionary<string, byte[]>
        {
            ["payload/sub/file.bin"] = [0xAB, 0xCD]
        };
        var migration = new MigrationResult(
            new Dictionary<string, string> { ["Program.cs"] = "// ok" },
            [],
            payloads);

        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console, migration);
        var settings = new Settings.MigrateSettings
        {
            FilePath = bundlePath,
            OutputPath = outDir,
            FalkForgeSourcePath = "../../src"
        };

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, result);
        Assert.True(File.Exists(Path.Combine(outDir, "payload", "sub", "file.bin")));
    }

    // ---------------------------------------------------------------------------
    // FIX C: IO failure → RuntimeError + no stack trace in error output
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When the output directory path already exists as a *file*, Directory.CreateDirectory
    /// throws IOException. The command must catch it, return RuntimeError, and emit
    /// only the exception message — no stack trace, no internal paths.
    /// </summary>
    [Fact]
    public void Execute_OutputDirIsExistingFile_ReturnsRuntimeError()
    {
        var bundlePath = BuildRealBundle();

        // Create a file at outDir path so Directory.CreateDirectory throws IOException.
        var outDir = Path.Combine(_tempDir, "not-a-dir.txt");
        File.WriteAllText(outDir, "I am a file, not a directory.");

        var migration = new MigrationResult(
            new Dictionary<string, string> { ["Program.cs"] = "// ok" },
            [],
            new Dictionary<string, byte[]>());

        var console = new TestConsoleOutput();
        var command = new MigrateCommand(console, migration);
        var settings = new Settings.MigrateSettings
        {
            FilePath = bundlePath,
            OutputPath = outDir,
            FalkForgeSourcePath = "../../src"
        };

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, result);

        // Must emit an error message with no stack-trace markers.
        Assert.NotEmpty(console.Errors);
        Assert.DoesNotContain(console.Errors, e => e.Contains("   at "));
    }
}
