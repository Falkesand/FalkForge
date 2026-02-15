using System.Diagnostics.CodeAnalysis;
using FalkInstaller.Cli.Settings;
using FalkInstaller.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkInstaller.Cli.Commands;

/// <summary>
/// Loads a C# installer definition and runs validation without producing output.
/// </summary>
public sealed class ValidateCommand : Command<ValidateSettings>
{
    private readonly IConsoleOutput _console;

    public ValidateCommand() : this(new SpectreConsoleOutput()) { }

    public ValidateCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] ValidateSettings settings)
    {
        var projectPath = Path.GetFullPath(settings.ProjectPath);

        if (!File.Exists(projectPath))
        {
            _console.WriteError($"File not found: {projectPath}");
            return ExitCodes.RuntimeError;
        }

        if (settings.Verbose)
            _console.MarkupLine($"[grey]Loading project: {Markup.Escape(projectPath)}[/]");

        var loadResult = ScriptLoader.LoadPackageModel(projectPath);
        if (loadResult.IsFailure)
        {
            _console.WriteError(loadResult.Error.Message);
            return ExitCodes.FromErrorKind(loadResult.Error.Kind);
        }

        var package = loadResult.Value;
        var validation = ModelValidator.Validate(package);

        foreach (var warning in validation.Warnings)
        {
            _console.MarkupLine($"[yellow]Warning {warning.Code}:[/] {Markup.Escape(warning.Message)}");
        }

        foreach (var error in validation.Errors)
        {
            _console.MarkupLine($"[red]Error {error.Code}:[/] {Markup.Escape(error.Message)}");
        }

        if (!validation.IsValid)
        {
            _console.MarkupLine($"[red]Validation failed with {validation.Errors.Count()} error(s).[/]");
            return ExitCodes.ValidationFailure;
        }

        _console.MarkupLine("[green]Validation passed.[/]");
        return ExitCodes.Success;
    }
}
