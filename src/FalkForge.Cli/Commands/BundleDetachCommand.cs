using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Compiler.Bundle.Compilation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Detaches a FALKBUNDLE EXE into a bare PE stub and a data file for external code signing.
/// </summary>
public sealed class BundleDetachCommand : Command<BundleDetachSettings>
{
    private readonly IConsoleOutput _console;

    public BundleDetachCommand() : this(new SpectreConsoleOutput()) { }

    public BundleDetachCommand(IConsoleOutput console)
    {
        _console = console;
    }

    protected override int Execute([NotNull] CommandContext context, [NotNull] BundleDetachSettings settings, CancellationToken cancellationToken)
    {
        var bundlePath = Path.GetFullPath(settings.BundlePath);
        var stubPath = Path.GetFullPath(settings.StubPath);
        var dataPath = Path.GetFullPath(settings.DataPath);

        _console.MarkupLine($"[grey]Detaching bundle: {Markup.Escape(bundlePath)}[/]");

        var result = BundleDetacher.Detach(bundlePath, stubPath, dataPath);

        if (result.IsFailure)
        {
            _console.WriteError(result.Error.Message);
            return ExitCodes.CompilationError;
        }

        _console.MarkupLine($"[green]Bundle detached successfully.[/]");
        _console.MarkupLine($"  Stub: {Markup.Escape(stubPath)}");
        _console.MarkupLine($"  Data: {Markup.Escape(dataPath)}");

        return ExitCodes.Success;
    }
}
