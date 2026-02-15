using Spectre.Console;

namespace FalkForge.Cli;

/// <summary>
/// Production implementation of <see cref="IConsoleOutput"/> that delegates to Spectre.Console.
/// </summary>
public sealed class SpectreConsoleOutput : IConsoleOutput
{
    public void MarkupLine(string markup) => AnsiConsole.MarkupLine(markup);
    public void WriteLine(string text) => AnsiConsole.WriteLine(text);
    public void WriteError(string text) => AnsiConsole.MarkupLine($"[red]{Markup.Escape(text)}[/]");
}
