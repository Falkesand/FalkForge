using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
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

    protected override int Execute([NotNull] CommandContext context, [NotNull] ExtractSettings settings, CancellationToken cancellationToken)
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

        // Trust binding (C14 Stage 2, §1.4): before extracting any payload, bind the value the extractor
        // trusts (the unsigned overlay TOC hash) to the ECDSA-signed manifest hash. Without this, a validly
        // signed bundle whose payload bytes + TOC hash were rewritten after signing would extract the
        // tampered bytes. The CLI has no baked publisher pin, so this is inspection-grade (consistency +
        // coverage): an empty trusted set still catches a post-signing overlay tamper (INT006) and an
        // uncovered appended payload (INT004). An unsigned bundle passes through.
        var trust = BundleTrustVerifier.VerifyBundleContent(content, System.Collections.Frozen.FrozenSet<string>.Empty);
        if (trust.IsFailure)
        {
            _console.WriteError(trust.Error.Message);
            return ExitCodes.FromErrorKind(trust.Error.Kind);
        }

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
        var rejectedCount = 0;
        foreach (var entry in targetEntries)
        {
            // entry.PackageId comes from the bundle's own TOC, which a crafted bundle fully
            // controls. Without containment, a PackageId of "..\..\evil" would write outside
            // outputDir (zip-slip / path traversal, OWASP A03). The pre-check here gives the
            // CLI its skip-and-report convention (hostile entries skipped, safe ones extracted,
            // non-zero exit); BundleReader's contained overload below enforces the same
            // containment again at the engine choke point (defense in depth).
            if (!TryResolvePackagePaths(outputDir, entry.PackageId, out _, out _))
            {
                _console.WriteError($"Package id '{entry.PackageId}' would escape the output directory. Skipping.");
                rejectedCount++;
                continue;
            }

            // Single-pass: streams decompressed bytes to the file while verifying SHA-256;
            // deletes the partial file and fails on mismatch.
            var payloadResult = BundleReader.ExtractPayloadToFile(
                exePath, entry, outputDir, Path.Combine(entry.PackageId, entry.PackageId));
            if (payloadResult.IsFailure)
            {
                _console.WriteError($"Failed to extract '{entry.PackageId}': {payloadResult.Error.Message}");
                return ExitCodes.FromErrorKind(payloadResult.Error.Kind);
            }

            extracted++;

            _console.MarkupLine($"  [grey]{Markup.Escape(entry.PackageId)}[/] ({FormatSize(entry.OriginalSize)})");
        }

        if (rejectedCount > 0)
        {
            _console.WriteError($"{rejectedCount} path-traversal attempt(s) detected and blocked. Extraction output is incomplete.");
            return ExitCodes.ValidationFailure;
        }

        _console.MarkupLine($"[green]Extracted {extracted} payload(s) to:[/] {Markup.Escape(outputDir)}");
        return ExitCodes.Success;
    }

    /// <summary>
    /// Resolves the package extraction directory and target file path for
    /// <paramref name="packageId"/> relative to <paramref name="outputDir"/>, and verifies both
    /// stay strictly inside it. Internal (not private) so tests can exercise the containment
    /// check directly with a fabricated hostile PackageId, without needing a real malicious
    /// bundle file.
    /// </summary>
    internal static bool TryResolvePackagePaths(
        string outputDir,
        string packageId,
        [NotNullWhen(true)] out string? packageDir,
        [NotNullWhen(true)] out string? targetPath)
    {
        var relativeKey = Path.Combine(packageId, packageId);
        if (!ContainedPathResolver.TryResolveContained(outputDir, relativeKey, out var resolved))
        {
            packageDir = null;
            targetPath = null;
            return false;
        }

        targetPath = resolved;
        packageDir = Path.GetDirectoryName(resolved)!;
        return true;
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
