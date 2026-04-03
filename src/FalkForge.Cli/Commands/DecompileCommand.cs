using System.Diagnostics.CodeAnalysis;
using FalkForge;
using FalkForge.Cli.Settings;
using FalkForge.Decompiler;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Decompiles an MSI or bundle EXE file into C# source code.
/// MSI decompilation uses <see cref="MsiDecompiler"/> (Windows-only: requires msi.dll P/Invoke).
/// Bundle EXE decompilation uses <see cref="BundleDecompiler"/> (cross-platform).
/// </summary>
public sealed class DecompileCommand : Command<DecompileSettings>
{
    private readonly IConsoleOutput _console;

    public DecompileCommand() : this(new SpectreConsoleOutput()) { }

    public DecompileCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] DecompileSettings settings, CancellationToken cancellationToken)
    {
        var filePath = Path.GetFullPath(settings.FilePath);

        if (!File.Exists(filePath))
        {
            _console.WriteError($"File not found: {filePath}");
            return ExitCodes.RuntimeError;
        }

        var extension = Path.GetExtension(filePath);

        if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
            return DecompileMsi(filePath, settings.OutputPath);

        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return DecompileBundle(filePath, settings.OutputPath);

        if (extension.Equals(".msix", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".msixbundle", StringComparison.OrdinalIgnoreCase))
        {
            _console.WriteError("MSIX decompilation is not yet supported.");
            return ExitCodes.RuntimeError;
        }

        _console.WriteError($"Unsupported file extension '{extension}'. Expected .msi, .exe, .msix, or .msixbundle.");
        return ExitCodes.RuntimeError;
    }

    private int DecompileMsi(string msiPath, string? outputPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            _console.WriteError("MSI decompilation requires Windows (msi.dll).");
            return ExitCodes.RuntimeError;
        }

        _console.MarkupLine($"[grey]Decompiling MSI: {Markup.Escape(msiPath)}[/]");

        var decompiler = new MsiDecompiler();
        var result = decompiler.DecompileToCSharp(msiPath);

        return WriteResult(result, outputPath);
    }

    private int DecompileBundle(string exePath, string? outputPath)
    {
        _console.MarkupLine($"[grey]Decompiling bundle: {Markup.Escape(exePath)}[/]");

        // Try FALKBUNDLE first (cross-platform)
        var falkResult = new BundleDecompiler().DecompileToCSharp(exePath);
        if (falkResult.IsSuccess)
            return WriteResult(falkResult, outputPath);

        // If FALKBUNDLE failed, try WiX Burn (Windows-only, requires cabinet.dll)
        if (!OperatingSystem.IsWindows())
        {
            _console.WriteError("Bundle decompilation requires Windows for WiX Burn bundles.");
            return ExitCodes.RuntimeError;
        }

        var wixResult = new WixBundleDecompiler().DecompileToCSharp(exePath);
        return WriteResult(wixResult, outputPath);
    }

    private int WriteResult(Result<string> result, string? outputPath)
    {
        if (result.IsFailure)
        {
            _console.WriteError(result.Error.Message);
            return ExitCodes.FromErrorKind(result.Error.Kind);
        }

        var source = result.Value;

        if (outputPath is not null)
        {
            var fullOutputPath = Path.GetFullPath(outputPath);
            var outputDir = Path.GetDirectoryName(fullOutputPath);
            if (outputDir is not null && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            File.WriteAllText(fullOutputPath, source);
            _console.MarkupLine($"[green]Decompiled to:[/] {Markup.Escape(fullOutputPath)}");
        }
        else
        {
            _console.WriteLine(source);
        }

        return ExitCodes.Success;
    }
}
