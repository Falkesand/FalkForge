using System.Diagnostics.CodeAnalysis;
using FalkInstaller.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkInstaller.Cli.Commands;

/// <summary>
/// Compiles a C# installer definition into an MSI or bundle.
/// Uses Roslyn scripting to evaluate the .cs file and invoke the appropriate compiler.
/// </summary>
public sealed class BuildCommand : Command<BuildSettings>
{
    private readonly IConsoleOutput _console;

    public BuildCommand() : this(new SpectreConsoleOutput()) { }

    public BuildCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] BuildSettings settings)
    {
        var projectPath = Path.GetFullPath(settings.ProjectPath);

        if (!File.Exists(projectPath))
        {
            _console.WriteError($"File not found: {projectPath}");
            return ExitCodes.RuntimeError;
        }

        if (settings.Verbose)
            _console.MarkupLine($"[grey]Loading project: {Markup.Escape(projectPath)}[/]");

        var outputPath = settings.OutputPath ?? Directory.GetCurrentDirectory();

        var loadResult = ScriptLoader.LoadAndBuild(projectPath, outputPath, settings.Configuration);
        if (loadResult.IsFailure)
        {
            _console.WriteError(loadResult.Error.Message);
            return ExitCodes.FromErrorKind(loadResult.Error.Kind);
        }

        _console.MarkupLine($"[green]Build succeeded:[/] {Markup.Escape(loadResult.Value)}");
        return ExitCodes.Success;
    }
}
