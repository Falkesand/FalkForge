namespace FalkForge.Cli;

/// <summary>
/// Abstracts console output for testability.
/// Production implementation delegates to <c>Spectre.Console.AnsiConsole</c>.
/// </summary>
public interface IConsoleOutput
{
    void MarkupLine(string markup);
    void WriteLine(string text);
    void WriteError(string text);
}
