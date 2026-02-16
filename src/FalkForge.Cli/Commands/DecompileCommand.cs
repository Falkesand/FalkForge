using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Decompiler;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Decompiles an MSI file into C# source code using <see cref="MsiDecompiler"/>.
/// Windows-only: requires msi.dll P/Invoke.
/// </summary>
public sealed class DecompileCommand : Command<DecompileSettings>
{
    private readonly IConsoleOutput _console;

    public DecompileCommand() : this(new SpectreConsoleOutput()) { }

    public DecompileCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] DecompileSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            _console.WriteError("The decompile command requires Windows (msi.dll).");
            return ExitCodes.RuntimeError;
        }

        var msiPath = Path.GetFullPath(settings.MsiPath);

        if (!File.Exists(msiPath))
        {
            _console.WriteError($"File not found: {msiPath}");
            return ExitCodes.RuntimeError;
        }

        _console.MarkupLine($"[grey]Decompiling: {Markup.Escape(msiPath)}[/]");

        var decompiler = new MsiDecompiler();
        var result = decompiler.DecompileToCSharp(msiPath);
        if (result.IsFailure)
        {
            _console.WriteError(result.Error.Message);
            return ExitCodes.FromErrorKind(result.Error.Kind);
        }

        var source = result.Value;

        if (settings.OutputPath is not null)
        {
            var outputPath = Path.GetFullPath(settings.OutputPath);
            var outputDir = Path.GetDirectoryName(outputPath);
            if (outputDir is not null && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            File.WriteAllText(outputPath, source);
            _console.MarkupLine($"[green]Decompiled to:[/] {Markup.Escape(outputPath)}");
        }
        else
        {
            _console.WriteLine(source);
        }

        return ExitCodes.Success;
    }
}
