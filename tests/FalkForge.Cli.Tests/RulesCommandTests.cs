using System.Collections.Frozen;
using System.Text.Json;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using FalkForge.Validation;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for <c>forge rules list</c> and <c>forge rules explain</c>.
/// Commands are invoked directly (not through CommandApp) so we can inject
/// a <see cref="TestConsoleOutput"/> and control the JSON sink.
/// </summary>
public sealed class RulesCommandTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static CommandContext ListContext() =>
        new([], new EmptyRemainingArguments(), "list", null);

    private static CommandContext ExplainContext() =>
        new([], new EmptyRemainingArguments(), "explain", null);

    private static (int exitCode, TestConsoleOutput console) RunList(RulesListSettings settings)
    {
        var console = new TestConsoleOutput();
        var command = new RulesListCommand(console);
        var exitCode = command.Execute(ListContext(), settings, CancellationToken.None);
        return (exitCode, console);
    }

    private static (int exitCode, JsonDocument doc) RunListJson(RulesListSettings settings)
    {
        using var sink = new System.IO.StringWriter();
        var console = new TestConsoleOutput();
        var command = new RulesListCommand(console, jsonSink: sink);
        var exitCode = command.Execute(ListContext(), settings, CancellationToken.None);
        return (exitCode, JsonDocument.Parse(sink.ToString().Trim()));
    }

    private static (int exitCode, TestConsoleOutput console) RunExplain(RulesExplainSettings settings)
    {
        var console = new TestConsoleOutput();
        var command = new RulesExplainCommand(console);
        var exitCode = command.Execute(ExplainContext(), settings, CancellationToken.None);
        return (exitCode, console);
    }

    // ── forge rules list (default target = package) ──────────────────────────

    [Fact]
    public void RulesListCommand_default_target_lists_package_rules()
    {
        // PKG001 is always present in the package catalog.
        var (exitCode, console) = RunList(new RulesListSettings());

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(
            console.AllOutput.Any(line => line.Contains("PKG001", StringComparison.Ordinal)),
            "Expected PKG001 in default (package) rules output.");
    }

    [Fact]
    public void RulesListCommand_target_patch_lists_msp_rules()
    {
        // When --target patch, MSP001 must appear and PKG001 must NOT appear.
        var (exitCode, console) = RunList(new RulesListSettings { Target = "patch" });

        Assert.Equal(ExitCodes.Success, exitCode);

        var allOutput = console.AllOutput.ToList();
        Assert.True(
            allOutput.Any(line => line.Contains("MSP001", StringComparison.Ordinal)),
            "Expected MSP001 in patch rules output.");
        Assert.False(
            allOutput.Any(line => line.Contains("PKG001", StringComparison.Ordinal)),
            "PKG001 must NOT appear in patch rules output.");
    }

    [Fact]
    public void RulesListCommand_section_filter_filters()
    {
        // --section Service should show only Service-section rules (SVC*).
        var (exitCode, console) = RunList(new RulesListSettings { Section = "Service" });

        Assert.Equal(ExitCodes.Success, exitCode);

        var allOutput = console.AllOutput.ToList();
        // At least one Service rule must appear.
        Assert.True(
            allOutput.Any(line => line.Contains("SVC", StringComparison.Ordinal)),
            "Expected at least one SVC rule when filtering by section=Service.");
        // No Package-section rules (PKG*) should appear.
        Assert.False(
            allOutput.Any(line => line.Contains("PKG", StringComparison.Ordinal)),
            "PKG rules must NOT appear when filtering by section=Service.");
    }

    [Fact]
    public void RulesListCommand_json_output_is_well_formed()
    {
        // --json must produce an array of rule objects with at minimum id, severity, section, title.
        var (exitCode, doc) = RunListJson(new RulesListSettings { Json = true });

        Assert.Equal(ExitCodes.Success, exitCode);

        var root = doc.RootElement;
        // Root should be an array.
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.True(root.GetArrayLength() > 0, "Expected at least one rule in JSON output.");

        var first = root.EnumerateArray().First();
        Assert.True(first.TryGetProperty("id", out _), "Rule object must have 'id'.");
        Assert.True(first.TryGetProperty("severity", out _), "Rule object must have 'severity'.");
        Assert.True(first.TryGetProperty("section", out _), "Rule object must have 'section'.");
        Assert.True(first.TryGetProperty("title", out _), "Rule object must have 'title'.");
        Assert.True(first.TryGetProperty("description", out _), "Rule object must have 'description'.");
    }

    // ── forge rules explain ───────────────────────────────────────────────────

    [Fact]
    public void RulesExplainCommand_existing_rule_prints_metadata()
    {
        var (exitCode, console) = RunExplain(new RulesExplainSettings { RuleId = "PKG001" });

        Assert.Equal(ExitCodes.Success, exitCode);

        var allOutput = console.AllOutput.ToList();
        Assert.True(
            allOutput.Any(line => line.Contains("PKG001", StringComparison.Ordinal)),
            "Expected PKG001 in explain output.");
        // Must include title and description text (at least one more non-ID line).
        Assert.True(allOutput.Count >= 2, "Expected multiple lines of metadata.");
    }

    [Fact]
    public void RulesExplainCommand_unknown_rule_returns_exit_1()
    {
        var (exitCode, console) = RunExplain(new RulesExplainSettings { RuleId = "XYZNOTREAL999" });

        Assert.Equal(ExitCodes.ValidationFailure, exitCode);
        Assert.True(
            console.Errors.Any(e => e.Contains("XYZNOTREAL999", StringComparison.Ordinal)),
            "Expected rule ID echoed in error message.");
    }
}
