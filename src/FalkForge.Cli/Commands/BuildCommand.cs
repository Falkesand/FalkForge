using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Cli.WinGet;
using FalkForge.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Compiles an installer definition (.cs or .json) into an MSI or bundle.
/// Uses Roslyn scripting for .cs files and JsonConfigLoader for .json files.
/// </summary>
public sealed class BuildCommand : Command<BuildSettings>
{
    private readonly IConsoleOutput _console;
    private readonly string? _gitWorkingDirectory;

    public BuildCommand() : this(new SpectreConsoleOutput()) { }

    public BuildCommand(IConsoleOutput console, string? gitWorkingDirectory = null)
    {
        _console = console;
        _gitWorkingDirectory = gitWorkingDirectory;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] BuildSettings settings)
    {
        if (settings.Reproducible)
        {
            var epoch = ResolveSourceDateEpoch(_console, _gitWorkingDirectory);
            if (epoch is null)
                return ExitCodes.RuntimeError;

            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", epoch.Value.ToString());
        }

        if (settings.GenerateSbom)
            Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", "1");

        var projectPath = Path.GetFullPath(settings.ProjectPath);

        if (!File.Exists(projectPath))
        {
            _console.WriteError($"File not found: {projectPath}");
            return ExitCodes.RuntimeError;
        }

        if (settings.Verbose)
            _console.MarkupLine($"[grey]Loading project: {Markup.Escape(projectPath)}[/]");

        var outputPath = settings.OutputPath ?? Directory.GetCurrentDirectory();

        if (projectPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var jsonResult = JsonConfigLoader.LoadFromFile(projectPath);
            if (jsonResult.IsFailure)
            {
                _console.WriteError(jsonResult.Error.Message);
                return ExitCodes.FromErrorKind(jsonResult.Error.Kind);
            }

            var package = jsonResult.Value;
            _console.MarkupLine($"[green]Loaded JSON config:[/] {Markup.Escape(package.Name)} v{package.Version}");

            if (string.Equals(settings.Format, "msix", StringComparison.OrdinalIgnoreCase))
            {
                _console.WriteError("MSIX packages cannot be built from JSON configuration. Use the C# script API with Installer.BuildMsix() instead. See demo/15-msix-basic for an example.");
                return ExitCodes.ValidationFailure;
            }

            _console.MarkupLine("[yellow]MSI compilation from JSON is not yet supported.[/]");
            return ExitCodes.Success;
        }

        if (string.Equals(settings.Format, "msix", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows())
            {
                _console.WriteError("MSIX compilation requires Windows.");
                return ExitCodes.RuntimeError;
            }

            _console.MarkupLine("[yellow]MSIX compilation from .cs scripts requires calling Installer.BuildMsix() in the script.[/]");
            _console.MarkupLine("[grey]Use --format msi (default) for MSI output.[/]");
        }

        var packageResult = ScriptLoader.LoadPackageModel(projectPath);
        if (packageResult.IsFailure)
        {
            _console.WriteError(packageResult.Error.Message);
            return ExitCodes.FromErrorKind(packageResult.Error.Kind);
        }

        var loadResult = ScriptLoader.LoadAndBuild(projectPath, outputPath, settings.Configuration);
        if (loadResult.IsFailure)
        {
            _console.WriteError(loadResult.Error.Message);
            return ExitCodes.FromErrorKind(loadResult.Error.Kind);
        }

        _console.MarkupLine($"[green]Build succeeded:[/] {Markup.Escape(loadResult.Value)}");

        if (settings.GenerateWinGet)
        {
            var wingetResult = GenerateWinGetManifest(loadResult.Value, packageResult.Value, settings);
            if (wingetResult.IsFailure)
                _console.MarkupLine($"[yellow]Warning:[/] WinGet manifest generation failed: {Markup.Escape(wingetResult.Error.Message)}");
            else
                _console.MarkupLine("[green]WinGet manifest written alongside installer[/]");
        }

        return ExitCodes.Success;
    }

    private static Result<Unit> GenerateWinGetManifest(
        string installerPath,
        PackageModel package,
        BuildSettings settings)
    {
        try
        {
            using var fs = File.OpenRead(installerPath);
            var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));

            var installerUrl = settings.WinGetInstallerUrl
                ?? $"https://example.com/{Path.GetFileName(installerPath)}";

            var identifier = WinGetManifestGenerator.SanitizePackageIdentifier(
                $"{package.Manufacturer}.{package.Name}");

            var options = new WinGetManifestOptions
            {
                PackageIdentifier = identifier,
                PackageVersion = package.Version.ToString(),
                Publisher = package.Manufacturer,
                PackageName = package.Name,
                ShortDescription = $"{package.Name} installer",
                InstallerUrl = installerUrl,
                InstallerSha256 = sha256
            };

            var yamlPath = installerPath + ".winget.yaml";
            return WinGetManifestGenerator.GenerateToFile(options, yamlPath);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"WinGet: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves the SOURCE_DATE_EPOCH value for a reproducible build.
    /// Priority: SOURCE_DATE_EPOCH env var → git log HEAD timestamp.
    /// Returns null and writes an error if neither source is available.
    /// </summary>
    internal static long? ResolveSourceDateEpoch(IConsoleOutput console, string? gitWorkingDirectory = null)
    {
        var envValue = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        if (envValue is not null)
        {
            if (long.TryParse(envValue, out var parsed))
                return parsed;

            console.WriteError("RPR001: SOURCE_DATE_EPOCH is not a valid Unix timestamp.");
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo("git", "log -1 --format=%ct")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = gitWorkingDirectory ?? Directory.GetCurrentDirectory()
            };
            using var proc = Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd().Trim();

            if (!proc.WaitForExit(10_000))
            {
                proc.Kill();
                // fall through to RPR002
            }
            else if (proc.ExitCode == 0 && long.TryParse(output, out var gitEpoch))
            {
                return gitEpoch;
            }
        }
        catch
        {
            // git not available or failed — fall through to RPR002
        }

        console.WriteError("RPR002: --reproducible requires SOURCE_DATE_EPOCH env var or a git repository.");
        return null;
    }
}
