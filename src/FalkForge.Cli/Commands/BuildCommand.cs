using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Compiles an installer definition (.cs or .json) into an MSI or bundle.
/// Uses Roslyn scripting for .cs files and JsonConfigLoader for .json files.
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

        if (projectPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var jsonResult = JsonConfigLoader.LoadFromFile(projectPath);
            if (jsonResult.IsFailure)
            {
                _console.WriteError(jsonResult.Error.Message);
                return ExitCodes.FromErrorKind(jsonResult.Error.Kind);
            }

            var package = jsonResult.Value;
            _console.MarkupLine($"[green]Loaded JSON config:[/] {Markup.Escape(package.Name)} v{package.Version}");
            _console.MarkupLine("[yellow]MSI compilation from JSON is not yet supported.[/]");
            return ExitCodes.Success;
        }

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
