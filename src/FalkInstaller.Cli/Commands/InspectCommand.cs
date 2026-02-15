using System.Diagnostics.CodeAnalysis;
using FalkInstaller.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkInstaller.Cli.Commands;

/// <summary>
/// Opens an MSI database and displays metadata including summary info and table list.
/// Windows-only: requires msi.dll P/Invoke.
/// </summary>
public sealed class InspectCommand : Command<InspectSettings>
{
    private readonly IConsoleOutput _console;

    public InspectCommand() : this(new SpectreConsoleOutput()) { }

    public InspectCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] InspectSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            _console.WriteError("The inspect command requires Windows (msi.dll).");
            return ExitCodes.RuntimeError;
        }

        var msiPath = Path.GetFullPath(settings.MsiPath);

        if (!File.Exists(msiPath))
        {
            _console.WriteError($"File not found: {msiPath}");
            return ExitCodes.RuntimeError;
        }

        if (settings.Verbose)
            _console.MarkupLine($"[grey]Inspecting: {Markup.Escape(msiPath)}[/]");

        var inspectResult = MsiInspector.Inspect(msiPath);
        if (inspectResult.IsFailure)
        {
            _console.WriteError(inspectResult.Error.Message);
            return ExitCodes.FromErrorKind(inspectResult.Error.Kind);
        }

        var info = inspectResult.Value;
        _console.MarkupLine($"[bold]MSI:[/] {Markup.Escape(msiPath)}");
        _console.MarkupLine($"[bold]Product:[/] {Markup.Escape(info.ProductName ?? "(unknown)")}");
        _console.MarkupLine($"[bold]Manufacturer:[/] {Markup.Escape(info.Manufacturer ?? "(unknown)")}");
        _console.MarkupLine($"[bold]Version:[/] {Markup.Escape(info.Version ?? "(unknown)")}");
        _console.MarkupLine($"[bold]Product Code:[/] {Markup.Escape(info.ProductCode ?? "(unknown)")}");
        _console.MarkupLine($"[bold]Tables:[/] {info.TableCount}");

        if (settings.Verbose && info.TableNames.Count > 0)
        {
            _console.MarkupLine("[bold]Table list:[/]");
            foreach (var tableName in info.TableNames)
            {
                _console.MarkupLine($"  {Markup.Escape(tableName)}");
            }
        }

        return ExitCodes.Success;
    }
}
