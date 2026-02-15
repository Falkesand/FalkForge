namespace FalkInstaller.Cli.Tests;

/// <summary>
/// Test double for <see cref="IConsoleOutput"/> that captures all output lines.
/// </summary>
public sealed class TestConsoleOutput : IConsoleOutput
{
    private readonly List<string> _markupLines = [];
    private readonly List<string> _lines = [];
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> MarkupLines => _markupLines;
    public IReadOnlyList<string> Lines => _lines;
    public IReadOnlyList<string> Errors => _errors;
    public IEnumerable<string> AllOutput => _markupLines.Concat(_lines).Concat(_errors);

    public void MarkupLine(string markup) => _markupLines.Add(markup);
    public void WriteLine(string text) => _lines.Add(text);
    public void WriteError(string text) => _errors.Add(text);
}
