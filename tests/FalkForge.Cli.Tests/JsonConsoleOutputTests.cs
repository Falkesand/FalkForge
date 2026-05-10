namespace FalkForge.Cli.Tests;

using System.Text.Json;
using FalkForge.Cli;
using Xunit;

public sealed class JsonConsoleOutputTests
{
    [Fact]
    public void Buffers_Messages_With_Levels()
    {
        var output = new JsonConsoleOutput();

        output.WriteLine("hello");
        output.WriteError("boom");
        output.MarkupLine("[yellow]Warning XYZ:[/] something");
        output.MarkupLine("[red]Error ABC:[/] failure");
        output.MarkupLine("[green]Validation passed.[/]");
        output.MarkupLine("[grey]Loading project: foo[/]");

        Assert.Collection(output.Messages,
            m => { Assert.Equal("info", m.Level); Assert.Equal("hello", m.Text); },
            m => { Assert.Equal("error", m.Level); Assert.Equal("boom", m.Text); },
            m => { Assert.Equal("warning", m.Level); Assert.Equal("Warning XYZ: something", m.Text); },
            m => { Assert.Equal("error", m.Level); Assert.Equal("Error ABC: failure", m.Text); },
            m => { Assert.Equal("info", m.Level); Assert.Equal("Validation passed.", m.Text); },
            m => { Assert.Equal("debug", m.Level); Assert.Equal("Loading project: foo", m.Text); });
    }

    [Fact]
    public void WriteEnvelope_Emits_Valid_Json_With_Expected_Fields()
    {
        var output = new JsonConsoleOutput();
        output.WriteLine("starting");
        output.WriteError("bad input");

        var envelope = output.WriteEnvelope("validate", exitCode: 1);

        using var doc = JsonDocument.Parse(envelope);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("validate", root.GetProperty("command").GetString());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("info", messages[0].GetProperty("level").GetString());
        Assert.Equal("starting", messages[0].GetProperty("text").GetString());
        Assert.Equal("error", messages[1].GetProperty("level").GetString());
        Assert.Equal("bad input", messages[1].GetProperty("text").GetString());
    }

    [Fact]
    public void WriteEnvelope_With_Result_Map_Includes_Result()
    {
        var output = new JsonConsoleOutput();
        var result = new Dictionary<string, string?> { ["outputPath"] = "out.msi" };
        var envelope = output.WriteEnvelope("build", exitCode: 0, result: result);

        using var doc = JsonDocument.Parse(envelope);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("build", root.GetProperty("command").GetString());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("out.msi", root.GetProperty("result").GetProperty("outputPath").GetString());
    }

    [Fact]
    public void WriteEnvelope_Without_Result_Omits_Result_Field()
    {
        var output = new JsonConsoleOutput();
        var envelope = output.WriteEnvelope("validate", exitCode: 0);

        using var doc = JsonDocument.Parse(envelope);
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public void Markup_With_Unknown_Tag_Defaults_To_Info_Level()
    {
        var output = new JsonConsoleOutput();
        output.MarkupLine("[blue]Custom message[/]");
        var msg = Assert.Single(output.Messages);
        Assert.Equal("info", msg.Level);
        Assert.Equal("Custom message", msg.Text);
    }

    [Fact]
    public void Plain_Text_Without_Markup_Becomes_Info_Message()
    {
        var output = new JsonConsoleOutput();
        output.MarkupLine("no markup here");
        var msg = Assert.Single(output.Messages);
        Assert.Equal("info", msg.Level);
        Assert.Equal("no markup here", msg.Text);
    }
}
