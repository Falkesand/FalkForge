using System.Text.Json;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for the <c>--json</c> output flag on <see cref="InspectCommand"/>.
/// When <c>--json</c> is set the command must emit a single JSON envelope to the
/// injected sink and produce no output on the injected IConsoleOutput. The envelope
/// schema is: <c>{ "command", "exitCode", "messages": [{ "level", "text" }] }</c>.
/// </summary>
public sealed class InspectCommandJsonTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "inspect", null);

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (int exitCode, JsonDocument envelope) RunWithJsonFlag(
        string msiPath,
        TestConsoleOutput? spectreCapture = null)
    {
        using var sink = new System.IO.StringWriter();
        var console = spectreCapture ?? new TestConsoleOutput();
        var command = new InspectCommand(console, jsonSink: sink);
        var settings = new InspectSettings { MsiPath = msiPath, Json = true };
        var exitCode = command.Execute(CreateContext(), settings, CancellationToken.None);
        return (exitCode, JsonDocument.Parse(sink.ToString().Trim()));
    }

    // ── schema contract ───────────────────────────────────────────────────────

    [Fact]
    public void JsonFlag_NonExistentFile_EmitsValidJsonEnvelope()
    {
        var (_, doc) = RunWithJsonFlag("no_such_file_xyz.msi");

        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("command", out _));
        Assert.True(doc.RootElement.TryGetProperty("exitCode", out _));
        Assert.True(doc.RootElement.TryGetProperty("messages", out _));
    }

    [Fact]
    public void JsonFlag_NonExistentFile_CommandFieldIsInspect()
    {
        var (_, doc) = RunWithJsonFlag("no_such_file_xyz.msi");

        Assert.Equal("inspect", doc.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public void JsonFlag_NonExistentFile_ExitCodeIsRuntimeError()
    {
        var (exitCode, doc) = RunWithJsonFlag("no_such_file_xyz.msi");

        Assert.Equal(ExitCodes.RuntimeError, exitCode);
        Assert.Equal(ExitCodes.RuntimeError, doc.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void JsonFlag_NonExistentFile_MessagesContainErrorEntry()
    {
        var (_, doc) = RunWithJsonFlag("no_such_file_xyz.msi");

        var messages = doc.RootElement.GetProperty("messages");
        Assert.True(messages.GetArrayLength() > 0);

        var hasError = false;
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.GetProperty("level").GetString() == "error")
            {
                hasError = true;
                break;
            }
        }
        Assert.True(hasError, "Expected at least one error-level message.");
    }

    [Fact]
    public void JsonFlag_NonExistentFile_FileNotFoundInMessages()
    {
        var (_, doc) = RunWithJsonFlag("no_such_file_xyz.msi");

        var messages = doc.RootElement.GetProperty("messages");
        var hasFileNotFound = false;
        foreach (var msg in messages.EnumerateArray())
        {
            var text = msg.GetProperty("text").GetString() ?? string.Empty;
            if (text.Contains("File not found", StringComparison.OrdinalIgnoreCase))
            {
                hasFileNotFound = true;
                break;
            }
        }
        Assert.True(hasFileNotFound, "Expected 'File not found' in messages.");
    }

    // ── mutual exclusion ──────────────────────────────────────────────────────

    [Fact]
    public void JsonFlag_SpectreConsoleReceivesNoOutput()
    {
        var spectreCapture = new TestConsoleOutput();
        RunWithJsonFlag("no_such_file_xyz.msi", spectreCapture);

        Assert.Empty(spectreCapture.AllOutput);
    }

    // ── InspectSettings has --json flag ───────────────────────────────────────

    [Fact]
    public void InspectSettings_HasJsonProperty()
    {
        var settings = new InspectSettings { MsiPath = "test.msi", Json = true };
        Assert.True(settings.Json);
    }

    [Fact]
    public void InspectSettings_JsonDefaultIsFalse()
    {
        var settings = new InspectSettings { MsiPath = "test.msi" };
        Assert.False(settings.Json);
    }
}
