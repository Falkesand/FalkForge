using System.Runtime.Versioning;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Integration tests proving <c>forge decompile</c> wires the Decompiler's structured
/// logging (Phase 2 of the logging instrumentation design) through <see cref="ConsoleOutputLogger"/>:
/// <c>--verbose</c> surfaces the decompiler's Debug entries (per-table reads, emitter stages),
/// non-verbose does not, and the Info start/complete lines always show regardless of verbosity.
/// Builds a real MSI first (via <see cref="BuildCommand"/>, mirroring <c>BuildCommandLoggingTests</c>)
/// so the decompile path exercises the production <c>MsiTableAccess</c>, not a mock.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DecompileCommandLoggingTests
{
    private static CommandContext CreateContext(string commandName) =>
        new([], new EmptyRemainingArguments(), commandName, null);

    private static (string msiPath, string tempDir) BuildFixtureMsi(string label)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ForgeDecompileLog_{label}_{Guid.NewGuid():N}");
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
            Name = "DecompileLogFixtureApp",
            Manufacturer = "TestCorp",
            Version = new System.Version(1, 0, 0),
            UpgradeCode = new System.Guid("77777777-8888-9999-aaaa-bbbbbbbbbbbb"),
            DefaultInstallDirectory = KnownFolder.ProgramFiles / "TestCorp" / "DecompileLogFixtureApp",
        };
        builder.Feature("Main", f =>
        {
            f.Title = "Main";
            f.Files(fs =>
            {
                fs.To(KnownFolder.ProgramFiles / "TestCorp" / "DecompileLogFixtureApp");
                fs.Add("{{escapedPayload}}");
            });
        });
        builder.Build()
        """;
        File.WriteAllText(csxPath, script);

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);

        var buildConsole = new TestConsoleOutput();
        var buildCommand = new BuildCommand(buildConsole);
        var buildSettings = new BuildSettings { ProjectPath = csxPath, OutputPath = outputDir };
        var buildResult = buildCommand.Execute(CreateContext("build"), buildSettings, CancellationToken.None);
        Assert.Equal(ExitCodes.Success, buildResult);

        var msiPath = Directory.GetFiles(outputDir, "*.msi").Single();
        return (msiPath, tempDir);
    }

    [Fact]
    public void Execute_Verbose_SurfacesDecompilerDebugLogs()
    {
        var (msiPath, tempDir) = BuildFixtureMsi(nameof(Execute_Verbose_SurfacesDecompilerDebugLogs));
        try
        {
            var console = new TestConsoleOutput();
            var command = new DecompileCommand(console);
            var settings = new DecompileSettings { FilePath = msiPath, Verbose = true };

            var result = command.Execute(CreateContext("decompile"), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, result);

            // Debug entries from MsiDecompiler (per-table reads) and CSharpEmitter (emitter
            // stage) render as grey markup lines when --verbose is set.
            Assert.Contains(console.MarkupLines, l => l.Contains("[grey]") && l.Contains("MsiDecompiler"));
            Assert.Contains(console.MarkupLines, l => l.Contains("[grey]") && l.Contains("CSharpEmitter"));

            // The Info start/complete entries always show, verbose or not.
            Assert.Contains(console.Lines, l => l.Contains("Decompiling MSI"));
            Assert.Contains(console.Lines, l => l.Contains("Decompiled MSI to C# source"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Execute_NotVerbose_SuppressesDecompilerDebugLogsButKeepsInfo()
    {
        var (msiPath, tempDir) = BuildFixtureMsi(nameof(Execute_NotVerbose_SuppressesDecompilerDebugLogsButKeepsInfo));
        try
        {
            var console = new TestConsoleOutput();
            var command = new DecompileCommand(console);
            var settings = new DecompileSettings { FilePath = msiPath, Verbose = false };

            var result = command.Execute(CreateContext("decompile"), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, result);

            // No grey Debug lines from the decompiler pipeline (category MsiDecompiler /
            // CSharpEmitter) when --verbose is not set.
            Assert.DoesNotContain(console.MarkupLines, l =>
                l.Contains("MsiDecompiler") || l.Contains("CSharpEmitter"));

            // The Info start/complete entries still show -- they are not gated behind --verbose.
            Assert.Contains(console.Lines, l => l.Contains("Decompiling MSI"));
            Assert.Contains(console.Lines, l => l.Contains("Decompiled MSI to C# source"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
