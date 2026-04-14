using System.Runtime.Versioning;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Integration tests covering the <c>forge build &lt;fixture&gt;.csx</c> path.
/// These tests fail until <see cref="BuildCommand"/> routes the C# script
/// through <see cref="ScriptLoader"/> and <c>MsiCompiler</c>, and
/// <see cref="BuildSettings"/> accepts the <c>.csx</c> extension.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("SourceDateEpoch")]
public sealed class BuildCommandCsxIntegrationTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    [Fact]
    public void Execute_ValidCsxFixture_ProducesMsiOnDisk()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"ForgeBuildCsx_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var payloadDir = Path.Combine(tempDir, "payload");
            Directory.CreateDirectory(payloadDir);
            var payloadFile = Path.Combine(payloadDir, "app.exe");
            File.WriteAllText(payloadFile, "csx fixture payload");

            var csxPath = Path.Combine(tempDir, "installer.csx");
            // Script returns a PackageModel (final expression is the return value
            // of a Roslyn script). Uses absolute payload path to avoid cwd-sensitivity.
            var escapedPayload = payloadFile.Replace("\\", "\\\\");
            var script = $$"""
            using FalkForge;
            using FalkForge.Builders;
            using FalkForge.Models;

            var builder = new PackageBuilder
            {
                Name = "CsxFixtureApp",
                Manufacturer = "TestCorp",
                Version = new System.Version(1, 0, 0),
                UpgradeCode = new System.Guid("22222222-3333-4444-5555-666666666666"),
                DefaultInstallDirectory = KnownFolder.ProgramFiles / "TestCorp" / "CsxFixtureApp",
            };
            builder.Feature("Main", f =>
            {
                f.Title = "Main";
                f.Files(fs =>
                {
                    fs.To(KnownFolder.ProgramFiles / "TestCorp" / "CsxFixtureApp");
                    fs.Add("{{escapedPayload}}");
                });
            });
            builder.Build()
            """;
            File.WriteAllText(csxPath, script);

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new BuildSettings
            {
                ProjectPath = csxPath,
                OutputPath = outputDir,
            };

            var result = command.Execute(CreateContext(), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, result);
            var msiFiles = Directory.GetFiles(outputDir, "*.msi");
            Assert.NotEmpty(msiFiles);
            Assert.True(new FileInfo(msiFiles[0]).Length > 0, "MSI file is empty");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadPackageModel_ScriptWithCompileError_ReturnsCompilationErrorResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ForgeScriptErr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var csxPath = Path.Combine(tempDir, "broken.csx");
            // Intentionally malformed: references an undefined symbol, which the
            // Roslyn script engine must surface as a CompilationErrorException.
            File.WriteAllText(csxPath, "this_symbol_does_not_exist;");

            var result = ScriptLoader.LoadPackageModel(csxPath);

            Assert.True(result.IsFailure, "Expected compile-error script to produce a failure Result.");
            Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
            Assert.Contains("this_symbol_does_not_exist", result.Error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
