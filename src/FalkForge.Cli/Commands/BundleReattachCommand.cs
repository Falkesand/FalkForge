using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Compiler.Bundle.Compilation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Reattaches a signed PE stub with a detached bundle data file,
/// patching TOC offsets to account for stub size changes from code signing.
/// </summary>
public sealed class BundleReattachCommand : Command<BundleReattachSettings>
{
    private readonly IConsoleOutput _console;

    public BundleReattachCommand() : this(new SpectreConsoleOutput()) { }

    public BundleReattachCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] BundleReattachSettings settings, CancellationToken cancellationToken)
    {
        var stubPath = Path.GetFullPath(settings.StubPath);
        var dataPath = Path.GetFullPath(settings.DataPath);
        var outputPath = Path.GetFullPath(settings.OutputPath);

        _console.MarkupLine($"[grey]Reattaching bundle from: {Markup.Escape(stubPath)}[/]");

        var result = BundleDetacher.Reattach(stubPath, dataPath, outputPath);

        if (result.IsFailure)
        {
            _console.WriteError(result.Error.Message);
            return ExitCodes.CompilationError;
        }

        _console.MarkupLine($"[green]Bundle reattached successfully.[/]");
        _console.MarkupLine($"  Output: {Markup.Escape(outputPath)}");

        return ExitCodes.Success;
    }
}
