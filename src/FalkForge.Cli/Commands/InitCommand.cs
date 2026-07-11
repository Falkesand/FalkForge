using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Scaffolds a starter FalkForge installer project: a csproj referencing the single
/// <c>FalkForge</c> meta-package at the CLI's own single-source version, a minimal working
/// fluent installer <c>Program.cs</c> (MSI or EXE bundle), and a payload folder — so
/// <c>forge init &amp;&amp; dotnet run</c> yields a real installer. Existing files are never
/// overwritten without <c>--force</c>, and a refused init writes nothing.
/// </summary>
public sealed class InitCommand : Command<InitSettings>
{
    private readonly IConsoleOutput _console;

    public InitCommand() : this(new SpectreConsoleOutput()) { }

    public InitCommand(IConsoleOutput console)
    {
        _console = console;
    }

    protected override int Execute([NotNull] CommandContext context, [NotNull] InitSettings settings, CancellationToken cancellationToken)
    {
        var outDir = Path.GetFullPath(settings.OutputDir);

        string? publishDir = null;
        if (settings.FromPublish is not null)
        {
            publishDir = Path.GetFullPath(settings.FromPublish);
            if (!Directory.Exists(publishDir))
            {
                _console.WriteError($"--from-publish directory not found: {publishDir}");
                return ExitCodes.RuntimeError;
            }
        }

        var productName = ResolveProductName(settings.Name, outDir);
        var projectFileName = CreateProjectFileName(productName);
        var isBundle = settings.Type.Equals(InitSettings.BundleType, StringComparison.OrdinalIgnoreCase);
        var packageVersion = VersionInfo.CliVersion.Split('+')[0];

        var files = InitScaffolder.CreateFiles(
            productName, projectFileName, isBundle, packageVersion,
            includeSamplePayload: publishDir is null);

        // Clobber protection: check EVERYTHING before writing ANYTHING, so a refused init
        // never leaves a partial scaffold behind.
        var conflicts = files.Keys
            .Where(relative => File.Exists(Path.Combine(outDir, relative)))
            .ToList();
        if (publishDir is not null && Directory.Exists(Path.Combine(outDir, "payload")))
            conflicts.Add("payload" + Path.DirectorySeparatorChar);

        if (conflicts.Count > 0 && !settings.Force)
        {
            _console.WriteError(
                $"Refusing to overwrite existing files in {outDir}: " +
                $"{string.Join(", ", conflicts)}. Pass --force to overwrite.");
            return ExitCodes.ValidationFailure;
        }

        try
        {
            foreach (var (relative, content) in files)
            {
                var fullPath = Path.Combine(outDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, content);
            }

            if (publishDir is not null)
                CopyDirectory(publishDir, Path.Combine(outDir, "payload"));
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

        _console.MarkupLine($"[green]Scaffolded {(isBundle ? "EXE bundle" : "MSI")} installer project:[/] {Markup.Escape(outDir)}");
        _console.MarkupLine($"  {Markup.Escape(projectFileName)}.csproj  (references the FalkForge meta-package {Markup.Escape(packageVersion)})");
        _console.MarkupLine("  Program.cs");
        _console.MarkupLine(publishDir is null
            ? "  payload\\  (sample — replace with your application's files)"
            : $"  payload\\  (prefilled from {Markup.Escape(publishDir)})");
        _console.MarkupLine("");
        _console.MarkupLine("Next: [grey]dotnet run[/] in that directory builds the installer.");
        return ExitCodes.Success;
    }

    /// <summary>
    /// Product name: explicit <c>--name</c> wins; otherwise the output directory's name.
    /// </summary>
    private static string ResolveProductName(string? explicitName, string outDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
            return explicitName.Trim();

        var dirName = Path.GetFileName(Path.TrimEndingDirectorySeparator(outDir));
        return string.IsNullOrWhiteSpace(dirName) ? "MyInstaller" : dirName;
    }

    /// <summary>
    /// File-system-safe project file name derived from the product name (spaces and invalid
    /// characters become underscores; a leading digit is prefixed so MSBuild identifiers stay valid).
    /// </summary>
    private static string CreateProjectFileName(string productName)
    {
        var sanitized = FileNameSanitizer.Sanitize(productName);
        if (sanitized.Length > 0 && char.IsAsciiDigit(sanitized[0]))
            sanitized = "_" + sanitized;
        return sanitized.Length == 0 ? "MyInstaller" : sanitized;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, sourceFile);
            var targetFile = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: true);
        }
    }
}
