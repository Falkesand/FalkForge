using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using FalkForge.Cli.Settings;
using FalkForge.Compiler.Msi.Validation;
using FalkForge.Models;
using FalkForge.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Loads an installer definition (.cs or .json) and runs validation without producing output.
/// </summary>
public sealed class ValidateCommand : Command<ValidateSettings>
{
    private readonly IConsoleOutput _console;

    public ValidateCommand() : this(new SpectreConsoleOutput()) { }

    public ValidateCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] ValidateSettings settings, CancellationToken cancellationToken)
    {
        var projectPath = Path.GetFullPath(settings.ProjectPath);

        if (!File.Exists(projectPath))
        {
            _console.WriteError($"File not found: {projectPath}");
            return ExitCodes.RuntimeError;
        }

        var extension = Path.GetExtension(projectPath);
        if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            if (!settings.Ice)
            {
                _console.MarkupLine("[yellow]Use --ice flag to run ICE validation on .msi files.[/]");
                return ExitCodes.Success;
            }

            if (!OperatingSystem.IsWindows())
            {
                _console.WriteError("ICE validation requires Windows.");
                return ExitCodes.RuntimeError;
            }

            return RunIceValidation(projectPath, settings);
        }

        if (settings.Verbose)
            _console.MarkupLine($"[grey]Loading project: {Markup.Escape(projectPath)}[/]");

        Result<FalkForge.Models.PackageModel> loadResult;

        if (projectPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            loadResult = JsonConfigLoader.LoadFromFile(projectPath);
        else
            loadResult = ScriptLoader.LoadPackageModel(projectPath);

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

    [SupportedOSPlatform("windows")]
    private int RunIceValidation(string msiPath, ValidateSettings settings)
    {
        var config = new IceConfiguration
        {
            Enabled = true,
            CubFilePath = settings.IceCubPath,
            SuppressedIces = settings.SuppressIce?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
            WarningsAsErrors = settings.IceWarningsAsErrors,
            ReportPath = settings.IceReport
        };

        var validator = new IceValidator();
        var result = validator.Validate(msiPath, config);

        if (result.IsFailure)
        {
            _console.WriteError($"ICE validation failed: {result.Error.Message}");
            return ExitCodes.RuntimeError;
        }

        var iceResult = result.Value;

        if (!string.IsNullOrEmpty(config.ReportPath))
            IceReportExporter.Export(iceResult, config.ReportPath);

        if (iceResult.Messages.Count == 0)
        {
            _console.MarkupLine("[green]ICE validation passed with no issues.[/]");
            return ExitCodes.Success;
        }

        foreach (var msg in iceResult.Messages)
        {
            var severityColor = msg.Severity switch
            {
                IceMessageSeverity.Failure => "red",
                IceMessageSeverity.Error => "red",
                IceMessageSeverity.Warning => "yellow",
                _ => "grey"
            };

            var table = msg.Table is not null ? $" ({Markup.Escape(msg.Table)})" : string.Empty;
            _console.MarkupLine($"[{severityColor}]{msg.Severity} {Markup.Escape(msg.IceName)}:[/] {Markup.Escape(msg.Description)}{table}");
        }

        var errorCount = iceResult.Errors.Count + iceResult.Failures.Count;
        var warnCount = iceResult.Warnings.Count;
        _console.MarkupLine(iceResult.IsValid
            ? $"[green]{iceResult.Messages.Count} issue(s) ({warnCount} warning(s)). Validation PASSED.[/]"
            : $"[red]{iceResult.Messages.Count} issue(s) ({errorCount} error(s), {warnCount} warning(s)). Validation FAILED.[/]");

        return iceResult.IsValid ? ExitCodes.Success : ExitCodes.ValidationFailure;
    }
}
