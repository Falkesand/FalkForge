using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FalkForge.Cli;

/// <summary>
/// IConsoleOutput implementation that buffers messages instead of writing immediately.
/// When the host command is finished, call <see cref="WriteEnvelope"/> to render the
/// captured messages as a structured JSON document. Used by <c>--json</c> CLI mode so
/// CI/automation can parse a single deterministic envelope per invocation rather than
/// scraping Spectre.Console markup.
/// </summary>
public sealed class JsonConsoleOutput : IConsoleOutput
{
    private readonly List<JsonConsoleMessage> _messages = new();

    public IReadOnlyList<JsonConsoleMessage> Messages => _messages;

    public void MarkupLine(string markup)
    {
        var (level, text) = ParseMarkup(markup);
        _messages.Add(new JsonConsoleMessage(level, text));
    }

    public void WriteLine(string text)
        => _messages.Add(new JsonConsoleMessage("info", text));

    public void WriteError(string text)
        => _messages.Add(new JsonConsoleMessage("error", text));

    /// <summary>
    /// Renders an envelope describing the command run. The output is a single JSON
    /// document with deterministic field order suitable for piping or capturing in CI.
    /// Optional <paramref name="result"/> map carries command-specific key/value pairs
    /// (e.g. output file path) and is omitted from the document when null or empty.
    /// </summary>
    public string WriteEnvelope(string command, int exitCode, IReadOnlyDictionary<string, string?>? result = null)
    {
        var envelope = new JsonConsoleEnvelope(
            command,
            exitCode,
            _messages,
            result is { Count: > 0 } ? result : null);
        return JsonSerializer.Serialize(envelope, JsonContext.Default.JsonConsoleEnvelope);
    }

    private static (string Level, string Text) ParseMarkup(string markup)
    {
        // Spectre markup looks like "[yellow]Warning XYZ:[/] message" or "[red]Error...[/]".
        // Strip a single leading [colour]...[/] tag pair to derive a structured level when
        // present; otherwise default to "info" and pass the raw text through.
        var sb = new StringBuilder(markup.Length);
        var level = "info";
        var i = 0;
        if (i < markup.Length && markup[i] == '[')
        {
            var close = markup.IndexOf(']', i + 1);
            if (close > i)
            {
                var tag = markup[(i + 1)..close];
                level = MapTagToLevel(tag);
                i = close + 1;
            }
        }
        for (; i < markup.Length; i++)
        {
            var ch = markup[i];
            if (ch == '[' && i + 1 < markup.Length && markup[i + 1] == '/')
            {
                var close = markup.IndexOf(']', i);
                if (close > i)
                {
                    i = close;
                    continue;
                }
            }
            sb.Append(ch);
        }
        return (level, sb.ToString());
    }

    private static string MapTagToLevel(string tag) => tag switch
    {
        "red" => "error",
        "yellow" => "warning",
        "green" => "info",
        "grey" or "gray" => "debug",
        _ => "info"
    };
}

public sealed record JsonConsoleMessage(string Level, string Text);

public sealed record JsonConsoleEnvelope(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("exitCode")] int ExitCode,
    [property: JsonPropertyName("messages")] IReadOnlyList<JsonConsoleMessage> Messages,
    [property: JsonPropertyName("result")] IReadOnlyDictionary<string, string?>? Result);

[JsonSerializable(typeof(JsonConsoleEnvelope))]
[JsonSerializable(typeof(JsonConsoleMessage))]
[JsonSerializable(typeof(IReadOnlyList<JsonConsoleMessage>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string?>))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class JsonContext : JsonSerializerContext;
