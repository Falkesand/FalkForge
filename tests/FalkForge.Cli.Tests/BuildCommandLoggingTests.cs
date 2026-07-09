using System.Runtime.Versioning;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Integration tests proving <c>forge build</c> wires the Compiler.Msi pipeline's structured
/// logging (Phase 1 of the logging instrumentation design) through <see cref="ConsoleOutputLogger"/>:
/// <c>--verbose</c> surfaces the pipeline's Debug step entries, non-verbose does not, and the
/// Info start/complete lines always show regardless of verbosity.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("SourceDateEpoch")]
public sealed class BuildCommandLoggingTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    private static (string csxPath, string outputDir, string tempDir) CreateFixture(string label)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ForgeBuildLog_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var payloadDir = Path.Combine(tempDir, "payload");
        Directory.CreateDirectory(payloadDir);
        var payloadFile = Path.Combine(payloadDir, "app.exe");
        File.WriteAllText(payloadFile, $"fixture payload for {label}");

        var csxPath = Path.Combine(tempDir, "installer.csx");
        var escapedPayload = payloadFile.Replace("\\", "\\\\");
        var script = $$"""
        using FalkForge;
        using FalkForge.Builders;
        using FalkForge.Models;

        var builder = new PackageBuilder
        {
            Name = "LoggedFixtureApp",
            Manufacturer = "TestCorp",
            Version = new System.Version(1, 0, 0),
            UpgradeCode = new System.Guid("77777777-8888-9999-aaaa-bbbbbbbbbbbb"),
            DefaultInstallDirectory = KnownFolder.ProgramFiles / "TestCorp" / "LoggedFixtureApp",
        };
        builder.Feature("Main", f =>
        {
            f.Title = "Main";
            f.Files(fs =>
            {
                fs.To(KnownFolder.ProgramFiles / "TestCorp" / "LoggedFixtureApp");
                fs.Add("{{escapedPayload}}");
            });
        });
        builder.Build()
        """;
        File.WriteAllText(csxPath, script);

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);

        return (csxPath, outputDir, tempDir);
    }

    [Fact]
    public void Execute_Verbose_SurfacesPipelineStepDebugLogs()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (csxPath, outputDir, tempDir) = CreateFixture(nameof(Execute_Verbose_SurfacesPipelineStepDebugLogs));
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new BuildSettings
            {
                ProjectPath = csxPath,
                OutputPath = outputDir,
                Verbose = true,
            };

            var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, result);

            // Debug step-boundary entries from MsiAuthoring render as grey markup lines when
            // --verbose is set.
            Assert.Contains(console.MarkupLines, l => l.Contains("[grey]") && l.Contains("MsiAuthoring"));

            // The Info start/complete entries always show, verbose or not.
            Assert.Contains(console.Lines, l => l.Contains("Compiling package 'LoggedFixtureApp'"));
            Assert.Contains(console.Lines, l => l.Contains("Compile complete"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Execute_NotVerbose_SuppressesPipelineStepDebugLogsButKeepsInfo()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (csxPath, outputDir, tempDir) = CreateFixture(nameof(Execute_NotVerbose_SuppressesPipelineStepDebugLogsButKeepsInfo));
        try
        {
            var console = new TestConsoleOutput();
            var command = new BuildCommand(console);
            var settings = new BuildSettings
            {
                ProjectPath = csxPath,
                OutputPath = outputDir,
                Verbose = false,
            };

            var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, result);

            // No grey Debug step lines from the compiler pipeline (category MsiAuthoring /
            // MsiRecipeBuilder / CabinetBuilder) when --verbose is not set.
            Assert.DoesNotContain(console.MarkupLines, l =>
                l.Contains("MsiAuthoring") || l.Contains("MsiRecipeBuilder") || l.Contains("CabinetBuilder"));

            // The Info start/complete entries still show -- they are not gated behind --verbose.
            Assert.Contains(console.Lines, l => l.Contains("Compiling package 'LoggedFixtureApp'"));
            Assert.Contains(console.Lines, l => l.Contains("Compile complete"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
