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

    public MigrateCommand() : this(new SpectreConsoleOutput()) { }

    public MigrateCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] MigrateSettings settings, CancellationToken cancellationToken)
    {
        var filePath = Path.GetFullPath(settings.FilePath);

        if (!File.Exists(filePath))
        {
            _console.WriteError($"File not found: {filePath}");
            return ExitCodes.ValidationFailure;
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

        _console.MarkupLine($"[grey]Migrating: {Markup.Escape(filePath)}[/]");

        var generateResult = new MigrationProjectGenerator().Generate(
            filePath,
            new MigrationOptions(FalkForgeSourcePath: falkForgeSrc, ProjectName: projectName));

        if (generateResult.IsFailure)
        {
            _console.WriteError(generateResult.Error.Message);
            return ExitCodes.FromErrorKind(generateResult.Error.Kind);
        }

        var migration = generateResult.Value;

        Directory.CreateDirectory(outDir);
        foreach (var (relativePath, content) in migration.TextFiles)
        {
            var fullPath = Path.GetFullPath(Path.Combine(outDir, relativePath));
            var dir = Path.GetDirectoryName(fullPath)!;
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, content);
        }

        var resolvedBase = Path.GetFullPath(outDir + Path.DirectorySeparatorChar);
        foreach (var (key, bytes) in migration.Payloads)
        {
            var resolvedKey = Path.GetFullPath(Path.Combine(outDir, key));
            if (!resolvedKey.StartsWith(resolvedBase, StringComparison.OrdinalIgnoreCase))
            {
                _console.WriteError($"Payload key '{key}' would escape the output directory. Skipping.");
                continue;
            }
            var dir = Path.GetDirectoryName(resolvedKey)!;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(resolvedKey, bytes);
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
