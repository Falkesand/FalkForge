using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Opens an MSI database and displays metadata including summary info and table list.
/// Windows-only: requires msi.dll P/Invoke.
/// </summary>
public sealed class InspectCommand : Command<InspectSettings>
{
    private readonly IConsoleOutput _console;
    private readonly System.IO.TextWriter _jsonSink;

    public InspectCommand() : this(new SpectreConsoleOutput()) { }

    public InspectCommand(IConsoleOutput console, System.IO.TextWriter? jsonSink = null)
    {
        _console = console;
        _jsonSink = jsonSink ?? Console.Out;
    }

    protected override int Execute([NotNull] CommandContext context, [NotNull] InspectSettings settings, CancellationToken cancellationToken)
    {
        var jsonOutput = settings.Json ? new JsonConsoleOutput() : null;
        var output = (IConsoleOutput?)jsonOutput ?? _console;

        var exitCode = ExecuteCore(settings, output);

        if (jsonOutput is not null)
            _jsonSink.WriteLine(jsonOutput.WriteEnvelope("inspect", exitCode));

        return exitCode;
    }

    private static int ExecuteCore(InspectSettings settings, IConsoleOutput output)
    {
        if (!OperatingSystem.IsWindows())
        {
            output.WriteError("The inspect command requires Windows (msi.dll).");
            return ExitCodes.RuntimeError;
        }

        var msiPath = Path.GetFullPath(settings.MsiPath);

        if (!File.Exists(msiPath))
        {
            output.WriteError($"File not found: {msiPath}");
            return ExitCodes.RuntimeError;
        }

        if (settings.Verbose)
            output.MarkupLine($"[grey]Inspecting: {Markup.Escape(msiPath)}[/]");

        if (settings.ExtractSbom)
        {
            var sbomResult = MsiInspector.ExtractSbom(msiPath);
            if (sbomResult.IsFailure)
            {
                output.WriteError(sbomResult.Error.Message);
                return ExitCodes.FromErrorKind(sbomResult.Error.Kind);
            }

            output.WriteLine(sbomResult.Value);
            return ExitCodes.Success;
        }

        var inspectResult = MsiInspector.Inspect(msiPath);
        if (inspectResult.IsFailure)
        {
            output.WriteError(inspectResult.Error.Message);
            return ExitCodes.FromErrorKind(inspectResult.Error.Kind);
        }

        var info = inspectResult.Value;
        output.MarkupLine($"[bold]MSI:[/] {Markup.Escape(msiPath)}");
        output.MarkupLine($"[bold]Product:[/] {Markup.Escape(info.ProductName ?? "(unknown)")}");
        output.MarkupLine($"[bold]Manufacturer:[/] {Markup.Escape(info.Manufacturer ?? "(unknown)")}");
        output.MarkupLine($"[bold]Version:[/] {Markup.Escape(info.Version ?? "(unknown)")}");
        output.MarkupLine($"[bold]Product Code:[/] {Markup.Escape(info.ProductCode ?? "(unknown)")}");
        output.MarkupLine($"[bold]Tables:[/] {info.TableCount}");

        if (settings.Verbose && info.TableNames.Count > 0)
        {
            output.MarkupLine("[bold]Table list:[/]");
            foreach (var tableName in info.TableNames)
            {
                output.MarkupLine($"  {Markup.Escape(tableName)}");
            }
        }

        return ExitCodes.Success;
    }
}
