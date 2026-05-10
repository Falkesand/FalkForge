using System.Collections.Frozen;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using FalkForge.Models;
using FalkForge.Validation;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for the rule-engine flags added to <c>forge validate</c>:
/// <c>--ignore</c>, <c>--warn-as-error</c>, and <c>--stop-on-first-error</c>.
/// Uses a temporary .json config file so <see cref="ValidateCommand"/> can load
/// a <see cref="PackageModel"/> with known violations via <see cref="JsonConfigLoader"/>.
/// </summary>
public sealed class ValidateCommandRuleOptionsTests : IDisposable
{
    // ── minimal valid JSON that triggers PKG001 (Name missing) ──────────────
    // PKG001 = Error, PKG002 = Error (Manufacturer missing when Name present but here both missing)
    // PKG004 = Warning (version is 0.0.0)
    // We use a JSON file with an intentionally empty name to trigger PKG001.

    private static string WriteTemp(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"falkforge_test_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    // Minimal JSON: no Name → PKG001 Error, no Manufacturer → PKG002 Error
    // Version "0.0.0" → PKG004 Warning
    private const string MinimalNoNameJson = """
        {
          "product": {
            "name": "",
            "manufacturer": "",
            "version": "0.0.0",
            "upgradeCode": "A1B2C3D4-E5F6-7890-ABCD-EF1234567890"
          }
        }
        """;

    // Has a valid name but version "0.0.0" → only PKG004 Warning (and PKG002 Error for missing manufacturer)
    private const string WarningOnlyJson = """
        {
          "product": {
            "name": "Test Product",
            "manufacturer": "Test Manufacturer",
            "version": "0.0.0",
            "upgradeCode": "A1B2C3D4-E5F6-7890-ABCD-EF1234567890"
          }
        }
        """;

    private readonly List<string> _tempFiles = [];

    private string TempFile(string json)
    {
        var path = WriteTemp(json);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
            try { File.Delete(path); } catch { /* best effort */ }
    }

    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "validate", null);

    // ── --ignore ──────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateCommand_ignore_suppresses_named_rule()
    {
        // PKG001 (Name required) fires on MinimalNoNameJson.
        // With --ignore PKG001 it must be absent from output and must not cause failure
        // (PKG002 still fires — but we only care PKG001 is suppressed, exit code still 1 from PKG002).
        var path = TempFile(MinimalNoNameJson);
        var console = new TestConsoleOutput();
        var command = new ValidateCommand(console);
        var settings = new ValidateSettings
        {
            ProjectPath = path,
            IgnoreRules = ["PKG001"]
        };

        command.Execute(CreateContext(), settings, CancellationToken.None);

        // PKG001 must NOT appear in any output line.
        Assert.False(
            console.AllOutput.Any(line => line.Contains("PKG001", StringComparison.Ordinal)),
            "PKG001 must be suppressed by --ignore PKG001.");
    }

    // ── --warn-as-error ───────────────────────────────────────────────────────

    [Fact]
    public void ValidateCommand_warn_as_error_promotes_warning()
    {
        // WarningOnlyJson has valid Name + Manufacturer but version "0.0.0" → PKG004 Warning.
        // Without --warn-as-error the command returns Success (warnings don't fail).
        // With --warn-as-error PKG004 becomes an error → exit code 1.
        var path = TempFile(WarningOnlyJson);

        // First confirm baseline: no warn-as-error → success.
        var console1 = new TestConsoleOutput();
        var baselineResult = new ValidateCommand(console1).Execute(
            CreateContext(),
            new ValidateSettings { ProjectPath = path },
            CancellationToken.None);
        Assert.Equal(ExitCodes.Success, baselineResult);

        // Now with --warn-as-error.
        var console2 = new TestConsoleOutput();
        var promotedResult = new ValidateCommand(console2).Execute(
            CreateContext(),
            new ValidateSettings { ProjectPath = path, WarningsAsErrors = true },
            CancellationToken.None);
        Assert.Equal(ExitCodes.ValidationFailure, promotedResult);
    }

    // ── --stop-on-first-error ─────────────────────────────────────────────────

    [Fact]
    public void ValidateCommand_stop_on_first_error_returns_one_error()
    {
        // MinimalNoNameJson triggers at minimum PKG001 + PKG002 errors.
        // With --stop-on-first-error only ONE error must appear in console output.
        var path = TempFile(MinimalNoNameJson);
        var console = new TestConsoleOutput();
        var command = new ValidateCommand(console);
        var settings = new ValidateSettings
        {
            ProjectPath = path,
            StopOnFirstError = true
        };

        command.Execute(CreateContext(), settings, CancellationToken.None);

        // Count error-containing markup lines (each "Error PKGxxx:" line).
        var errorLines = console.AllOutput
            .Where(line => line.Contains("Error PKG", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(1, errorLines.Count);
    }
}
