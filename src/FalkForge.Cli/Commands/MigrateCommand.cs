using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Decompiler;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Migrates an existing installer (.msi, .msm, or .exe) to a buildable FalkForge C# project.
/// MSI/MSM migration is Windows-only (requires msi.dll P/Invoke via MsiDecompiler).
/// EXE bundle migration is cross-platform for native FalkForge bundles; WiX Burn is Windows-only.
/// </summary>
public sealed class MigrateCommand : Command<MigrateSettings>
{
    private readonly IConsoleOutput _console;

    // Test-seam: injects a pre-built MigrationResult, bypassing the generator.
    private readonly MigrationResult? _injectedResult;

    public MigrateCommand() : this(new SpectreConsoleOutput()) { }

    public MigrateCommand(IConsoleOutput console)
    {
        _console = console;
    }

    /// <summary>
    /// Test-seam constructor. Injects a pre-built <see cref="MigrationResult"/> so tests
    /// can exercise the write phase (containment checks, IO handling) in isolation without
    /// needing a real installer file to decompile.
    /// </summary>
    internal MigrateCommand(IConsoleOutput console, MigrationResult injectedResult)
    {
        _console = console;
        _injectedResult = injectedResult;
    }

    protected override int Execute([NotNull] CommandContext context, [NotNull] MigrateSettings settings, CancellationToken cancellationToken)
    {
        var filePath = Path.GetFullPath(settings.FilePath);

        if (!File.Exists(filePath))
        {
            _console.WriteError($"File not found: {filePath}");
            // FIX D: align with DecompileCommand convention — FileNotFound → RuntimeError.
            return ExitCodes.RuntimeError;
        }

        var extension = Path.GetExtension(filePath);

        if ((extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".msm", StringComparison.OrdinalIgnoreCase)) &&
            !OperatingSystem.IsWindows())
        {
            _console.WriteError("MSI/MSM migration requires Windows (msi.dll).");
            return ExitCodes.RuntimeError;
        }

        var falkForgeSrc = ResolveFalkForgeSourcePath(settings.FalkForgeSourcePath);
        if (falkForgeSrc is null)
        {
            _console.WriteError(
                "Could not locate the FalkForge src/ directory. Pass --falkforge-src <path> to specify it explicitly.");
            return ExitCodes.ValidationFailure;
        }

        var stem = Path.GetFileNameWithoutExtension(filePath);
        var projectName = FileNameSanitizer.Sanitize(stem);
        if (projectName.Length > 0 && char.IsDigit(projectName[0]))
            projectName = "_" + projectName;
        if (string.IsNullOrEmpty(projectName))
            projectName = "MigratedInstaller";

        var outDir = settings.OutputPath is not null
            ? Path.GetFullPath(settings.OutputPath)
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), stem + "-migrated"));

        MigrationResult migration;

        if (_injectedResult is not null)
        {
            // Test-seam: skip generation and use the pre-built result.
            migration = _injectedResult;
        }
        else
        {
            _console.MarkupLine($"[grey]Migrating: {Markup.Escape(filePath)}[/]");

            var generateResult = new MigrationProjectGenerator().Generate(
                filePath,
                new MigrationOptions(FalkForgeSourcePath: falkForgeSrc, ProjectName: projectName));

            if (generateResult.IsFailure)
            {
                _console.WriteError(generateResult.Error.Message);
                return ExitCodes.FromErrorKind(generateResult.Error.Kind);
            }

            migration = generateResult.Value;
        }

        // FIX C: wrap all file/directory IO so OS errors surface as clean messages, not stack traces.
        try
        {
            return WriteOutput(outDir, migration);
        }
        catch (IOException ex)
        {
            _console.WriteError(ex.Message);
            return ExitCodes.RuntimeError;
        }
        catch (UnauthorizedAccessException ex)
        {
            _console.WriteError(ex.Message);
            return ExitCodes.RuntimeError;
        }
    }

    /// <summary>
    /// Writes all text files and payloads from <paramref name="migration"/> into
    /// <paramref name="outDir"/>, applying path-containment checks on every entry.
    /// Returns <see cref="ExitCodes.ValidationFailure"/> if any traversal attempt is detected;
    /// <see cref="ExitCodes.Success"/> otherwise.
    /// </summary>
    private int WriteOutput(string outDir, MigrationResult migration)
    {
        Directory.CreateDirectory(outDir);

        var rejectedCount = 0;

        // FIX A: containment check on TextFiles (previously unchecked).
        foreach (var (relativePath, content) in migration.TextFiles)
        {
            if (!ContainedPathResolver.TryResolveContained(outDir, relativePath, out var fullPath))
            {
                // FIX B: log security-relevant rejection naming the offending key.
                _console.WriteError($"Text file key '{relativePath}' would escape the output directory. Skipping.");
                rejectedCount++;
                continue;
            }
            var dir = Path.GetDirectoryName(fullPath)!;
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, content);
        }

        // FIX A: replace OrdinalIgnoreCase StartsWith containment with OS-correct helper.
        foreach (var (key, bytes) in migration.Payloads)
        {
            if (!ContainedPathResolver.TryResolveContained(outDir, key, out var resolvedKey))
            {
                // FIX B: log security-relevant rejection naming the offending key.
                _console.WriteError($"Payload key '{key}' would escape the output directory. Skipping.");
                rejectedCount++;
                continue;
            }
            var dir = Path.GetDirectoryName(resolvedKey)!;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(resolvedKey, bytes);
        }

        // FIX B: any rejected entry → non-zero exit so CI sees the traversal attempt.
        if (rejectedCount > 0)
        {
            _console.WriteError($"{rejectedCount} path-traversal attempt(s) detected and blocked. Migration output is incomplete.");
            return ExitCodes.ValidationFailure;
        }

        _console.MarkupLine($"[green]Migration complete:[/] {Markup.Escape(outDir)}");
        _console.MarkupLine($"  Source files: {migration.TextFiles.Count}");
        _console.MarkupLine($"  Payload files: {migration.Payloads.Count}");
        if (migration.Unmapped.Count > 0)
            _console.MarkupLine($"  [yellow]Unmapped features: {migration.Unmapped.Count} — see MIGRATION-REPORT.md[/]");
        else
            _console.MarkupLine($"  See [grey]MIGRATION-REPORT.md[/] for details.");

        return ExitCodes.Success;
    }

    /// <summary>
    /// Resolves the FalkForge source path.
    /// Priority: explicit setting → walk-up from AppContext.BaseDirectory → walk-up from cwd.
    /// The locator looks for a directory containing <c>FalkForge.Core/FalkForge.Core.csproj</c>.
    /// </summary>
    private static string? ResolveFalkForgeSourcePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath;

        if (TryLocate(AppContext.BaseDirectory, out var found))
            return found;

        if (TryLocate(Directory.GetCurrentDirectory(), out found))
            return found;

        return null;
    }

    private static bool TryLocate(string startDir, [NotNullWhen(true)] out string? srcPath)
    {
        var current = new DirectoryInfo(startDir);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src");
            var marker = Path.Combine(candidate, "FalkForge.Core", "FalkForge.Core.csproj");
            if (File.Exists(marker))
            {
                srcPath = candidate;
                return true;
            }
            current = current.Parent;
        }
        srcPath = null;
        return false;
    }
}
