using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Engine.Protocol.Bundle;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Extracts files from an MSI/MSM database or payloads from a FalkForge EXE bundle.
/// MSI extraction uses <see cref="MsiExtractor"/> (Windows-only: requires msi.dll + cabinet.dll).
/// Bundle extraction uses <see cref="BundleReader"/> (cross-platform).
/// </summary>
public sealed class ExtractCommand : Command<ExtractSettings>
{
    private readonly IConsoleOutput _console;

    public ExtractCommand() : this(new SpectreConsoleOutput()) { }

    public ExtractCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] ExtractSettings settings, CancellationToken cancellationToken)
    {
        var filePath = Path.GetFullPath(settings.FilePath);

        if (!File.Exists(filePath))
        {
            _console.WriteError($"File not found: {filePath}");
            return ExitCodes.RuntimeError;
        }

        var extension = Path.GetExtension(filePath);

        if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".msm", StringComparison.OrdinalIgnoreCase))
            return ExtractMsi(filePath, settings.OutputPath!);

        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return ExtractBundle(filePath, settings);

        _console.WriteError($"Unsupported file extension '{extension}'. Expected .msi, .msm, or .exe.");
        return ExitCodes.RuntimeError;
    }

    private int ExtractMsi(string msiPath, string outputDir)
    {
        if (!OperatingSystem.IsWindows())
        {
            _console.WriteError("MSI extraction requires Windows (msi.dll + cabinet.dll).");
            return ExitCodes.RuntimeError;
        }

        _console.MarkupLine($"[grey]Extracting: {Markup.Escape(msiPath)}[/]");

        var fullOutputDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(fullOutputDir);

        var result = MsiExtractor.Extract(msiPath, fullOutputDir);
        if (result.IsFailure)
        {
            _console.WriteError(result.Error.Message);
            return ExitCodes.FromErrorKind(result.Error.Kind);
        }

        _console.MarkupLine($"[green]Extracted {result.Value} file(s) to:[/] {Markup.Escape(fullOutputDir)}");
        return ExitCodes.Success;
    }

    private int ExtractBundle(string exePath, ExtractSettings settings)
    {
        _console.MarkupLine($"[grey]Reading bundle: {Markup.Escape(exePath)}[/]");

        var bundleResult = BundleReader.Extract(exePath);
        if (bundleResult.IsFailure)
        {
            _console.WriteError(bundleResult.Error.Message);
            return ExitCodes.FromErrorKind(bundleResult.Error.Kind);
        }

        var content = bundleResult.Value;
        var entries = content.TocEntries;

        if (settings.ListOnly)
            return ListPackages(entries);

        var outputDir = Path.GetFullPath(settings.OutputPath!);
        Directory.CreateDirectory(outputDir);

        // Filter by --package if specified
        TocEntry[] targetEntries;
        if (settings.Packages is { Length: > 0 })
        {
            var requestedSet = new HashSet<string>(settings.Packages, StringComparer.OrdinalIgnoreCase);
            targetEntries = entries.Where(e => requestedSet.Contains(e.PackageId)).ToArray();

            var foundIds = new HashSet<string>(targetEntries.Select(e => e.PackageId), StringComparer.OrdinalIgnoreCase);
            var missing = requestedSet.Where(id => !foundIds.Contains(id)).ToList();
            if (missing.Count > 0)
            {
                _console.WriteError($"Package(s) not found: {string.Join(", ", missing)}");
                _console.WriteError($"Available packages: {string.Join(", ", entries.Select(e => e.PackageId))}");
                return ExitCodes.RuntimeError;
            }
        }
        else
        {
            targetEntries = entries;
        }

        var extracted = 0;
        foreach (var entry in targetEntries)
        {
            var payloadResult = BundleReader.ExtractPayload(exePath, entry);
            if (payloadResult.IsFailure)
            {
                _console.WriteError($"Failed to extract '{entry.PackageId}': {payloadResult.Error.Message}");
                return ExitCodes.FromErrorKind(payloadResult.Error.Kind);
            }

            var packageDir = Path.Combine(outputDir, entry.PackageId);
            Directory.CreateDirectory(packageDir);

            var targetPath = Path.Combine(packageDir, entry.PackageId);
            File.WriteAllBytes(targetPath, payloadResult.Value);
            extracted++;

            _console.MarkupLine($"  [grey]{Markup.Escape(entry.PackageId)}[/] ({FormatSize(entry.OriginalSize)})");
        }

        _console.MarkupLine($"[green]Extracted {extracted} payload(s) to:[/] {Markup.Escape(outputDir)}");
        return ExitCodes.Success;
    }

    private int ListPackages(TocEntry[] entries)
    {
        if (entries.Length == 0)
        {
            _console.WriteLine("Bundle contains no packages.");
            return ExitCodes.Success;
        }

        var header = string.Format("{0,-40} {1,15} {2,15}", "PackageId", "Original Size", "Compressed");
        _console.MarkupLine($"[bold]{header}[/]");
        foreach (var entry in entries)
        {
            _console.WriteLine(string.Format("{0,-40} {1,15} {2,15}",
                entry.PackageId, FormatSize(entry.OriginalSize), FormatSize(entry.CompressedSize)));
        }

        _console.MarkupLine($"[grey]{entries.Length} package(s)[/]");
        return ExitCodes.Success;
    }

    private static string FormatSize(long bytes)
    {
        const long kb = 1024;
        const long mb = 1024 * 1024;
        const long gb = 1024 * 1024 * 1024;

        return bytes switch
        {
            < kb => $"{bytes} B",
            < mb => $"{bytes / (double)kb:F1} KB",
            < gb => $"{bytes / (double)mb:F1} MB",
            _ => $"{bytes / (double)gb:F2} GB"
        };
    }
}
