using System.Runtime.Versioning;
using System.Text.Json;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for the <c>--dry-run</c> flag on <see cref="BuildCommand"/>. Dry-run runs
/// validation and planning but skips the actual MSI emit step. Exit code mirrors
/// the validation outcome (Success on clean, ValidationFailure on invalid input).
/// When combined with <c>--json</c>, the envelope carries a <c>dryRun: true</c>
/// flag so CI consumers can distinguish a dry-run envelope from a real build.
/// </summary>
[Collection("SourceDateEpoch")]
public sealed class BuildCommandDryRunTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    private static (string TempDir, string ProjectPath, string OutputDir) CreateValidJsonProject()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ForgeDryRun_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var payloadDir = Path.Combine(tempDir, "payload");
        Directory.CreateDirectory(payloadDir);
        File.WriteAllText(Path.Combine(payloadDir, "app.exe"), "fixture payload bytes");

        var projectPath = Path.Combine(tempDir, "installer.json");
        var json = """
        {
            "product": {
                "name": "DryRunFixtureApp",
                "manufacturer": "TestCorp",
                "version": "1.0.0",
                "upgradeCode": "11111111-2222-3333-4444-555555555555"
            },
            "installDirectory": "TestCorp\\DryRunFixtureApp",
            "features": [
                {
                    "id": "Main",
                    "title": "Main Feature",
                    "default": true,
                    "files": [
                        { "source": "payload/app.exe" }
                    ]
                }
            ]
        }
        """;
        File.WriteAllText(projectPath, json);

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);

        return (tempDir, projectPath, outputDir);
    }

    private static (string TempDir, string ProjectPath, string OutputDir) CreateInvalidJsonProject()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ForgeDryRunInvalid_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        // Missing required "product.name" — JsonConfigLoader returns Validation failure.
        var projectPath = Path.Combine(tempDir, "installer.json");
        var json = """
        {
            "product": {
                "manufacturer": "TestCorp",
                "version": "1.0.0",
                "upgradeCode": "11111111-2222-3333-4444-555555555555"
            }
        }
        """;
        File.WriteAllText(projectPath, json);

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);

        return (tempDir, projectPath, outputDir);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Build_DryRun_DoesNotWriteOutputFile()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (tempDir, projectPath, outputDir) = CreateValidJsonProject();
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new BuildSettings
            {
                ProjectPath = projectPath,
                OutputPath = outputDir,
                DryRun = true
            };

            var exitCode = command.Execute(CreateContext(), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
            var msiFiles = Directory.GetFiles(outputDir, "*.msi");
            Assert.Empty(msiFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Build_DryRun_PrintsSummary()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (tempDir, projectPath, outputDir) = CreateValidJsonProject();
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new BuildSettings
            {
                ProjectPath = projectPath,
                OutputPath = outputDir,
                DryRun = true
            };

            command.Execute(CreateContext(), settings, CancellationToken.None);

            var allOutput = string.Join("\n", console.AllOutput);
            Assert.Contains("dry run", allOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DryRunFixtureApp", allOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Build_DryRun_Json_ReturnsEnvelopeWithDryRunFlag()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (tempDir, projectPath, outputDir) = CreateValidJsonProject();
        try
        {
            using var sink = new StringWriter();
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console, jsonSink: sink);
            var settings = new BuildSettings
            {
                ProjectPath = projectPath,
                OutputPath = outputDir,
                DryRun = true,
                Json = true
            };

            var exitCode = command.Execute(CreateContext(), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);

            var doc = JsonDocument.Parse(sink.ToString().Trim());
            Assert.Equal("build", doc.RootElement.GetProperty("command").GetString());
            Assert.Equal(ExitCodes.Success, doc.RootElement.GetProperty("exitCode").GetInt32());

            // dryRun flag should live inside the result map (per JsonConsoleEnvelope schema).
            Assert.True(doc.RootElement.TryGetProperty("result", out var result), "envelope missing 'result' map");
            Assert.True(result.TryGetProperty("dryRun", out var dryRunProp), "result missing 'dryRun' key");
            Assert.Equal("true", dryRunProp.GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Build_DryRun_ValidationFailure_ReturnsValidationExit()
    {
        var (tempDir, projectPath, outputDir) = CreateInvalidJsonProject();
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new BuildSettings
            {
                ProjectPath = projectPath,
                OutputPath = outputDir,
                DryRun = true
            };

            var exitCode = command.Execute(CreateContext(), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.ValidationFailure, exitCode);
            var msiFiles = Directory.GetFiles(outputDir, "*.msi");
            Assert.Empty(msiFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
