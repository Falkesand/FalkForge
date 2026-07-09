using System.Text.Json;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for the <c>--json</c> output flag on <see cref="BuildCommand"/>.
/// When <c>--json</c> is set the command must emit a single JSON envelope to the
/// injected sink and produce no output on the injected IConsoleOutput. The envelope
/// schema is: <c>{ "command", "exitCode", "messages": [{ "level", "text" }] }</c>.
/// </summary>
[Collection("SourceDateEpoch")]
public sealed class BuildCommandJsonTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (int exitCode, JsonDocument envelope) RunWithJsonFlag(
        string projectPath,
        TestConsoleOutput? spectreCapture = null)
    {
        using var sink = new System.IO.StringWriter();
        var console = spectreCapture ?? new TestConsoleOutput();
        var command = new BuildCommand(console, jsonSink: sink);
        var settings = new BuildSettings { ProjectPath = projectPath, Json = true };
        var exitCode = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);
        return (exitCode, JsonDocument.Parse(sink.ToString().Trim()));
    }

    // ── schema contract ───────────────────────────────────────────────────────

    [Fact]
    public void JsonFlag_NonExistentFile_EmitsValidJsonEnvelope()
    {
        var (_, doc) = RunWithJsonFlag("no_such_file_xyz.cs");

        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("command", out _));
        Assert.True(doc.RootElement.TryGetProperty("exitCode", out _));
        Assert.True(doc.RootElement.TryGetProperty("messages", out _));
    }

    [Fact]
    public void JsonFlag_NonExistentFile_CommandFieldIsBuild()
    {
        var (_, doc) = RunWithJsonFlag("no_such_file_xyz.cs");

        Assert.Equal("build", doc.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public void JsonFlag_NonExistentFile_ExitCodeIsRuntimeError()
    {
        var (exitCode, doc) = RunWithJsonFlag("no_such_file_xyz.cs");

        Assert.Equal(ExitCodes.RuntimeError, exitCode);
        Assert.Equal(ExitCodes.RuntimeError, doc.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void JsonFlag_NonExistentFile_MessagesContainErrorEntry()
    {
        var (_, doc) = RunWithJsonFlag("no_such_file_xyz.cs");

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
        var (_, doc) = RunWithJsonFlag("no_such_file_xyz.cs");

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
        // The injected IConsoleOutput must receive zero calls when --json is active.
        var spectreCapture = new TestConsoleOutput();
        RunWithJsonFlag("no_such_file_xyz.cs", spectreCapture);

        Assert.Empty(spectreCapture.AllOutput);
    }
}
