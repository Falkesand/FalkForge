using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using FalkForge.Cli.Settings;
using FalkForge.Cli.WinGet;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Compiles an installer definition (.cs, .csx, or .json) into an MSI.
/// Uses Roslyn scripting for .cs/.csx files and JsonConfigLoader for .json files.
/// Dispatch lives in <see cref="BuildInputResolver"/>.
/// </summary>
public sealed class BuildCommand : Command<BuildSettings>
{
    // _console is swapped to a JsonConsoleOutput buffer when settings.Json is set so the
    // entire build run accumulates messages into a single envelope rendered at the end.
    private IConsoleOutput _console;
    private readonly string? _gitWorkingDirectory;
    private readonly System.IO.TextWriter _jsonSink;

    public BuildCommand() : this(new SpectreConsoleOutput()) { }

    public BuildCommand(IConsoleOutput console, string? gitWorkingDirectory = null, System.IO.TextWriter? jsonSink = null)
    {
        _console = console;
        _gitWorkingDirectory = gitWorkingDirectory;
        _jsonSink = jsonSink ?? Console.Out;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] BuildSettings settings, CancellationToken cancellationToken)
    {
        var originalConsole = _console;
        var jsonOutput = settings.Json ? new JsonConsoleOutput() : null;
        if (jsonOutput is not null)
            _console = jsonOutput;

        try
        {
            var exitCode = ExecuteInternal(settings, cancellationToken);
            if (jsonOutput is not null)
                _jsonSink.WriteLine(jsonOutput.WriteEnvelope("build", exitCode));
            return exitCode;
        }
        finally
        {
            _console = originalConsole;
        }
    }

    private int ExecuteInternal(BuildSettings settings, CancellationToken cancellationToken)
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

        if (settings.NoSign)
            Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", "1");

        var projectPath = Path.GetFullPath(settings.ProjectPath);

        if (!File.Exists(projectPath))
        {
            _console.WriteError($"File not found: {projectPath}");
            return ExitCodes.RuntimeError;
        }

        if (settings.Verbose)
            _console.MarkupLine($"[grey]Loading project: {Markup.Escape(projectPath)}[/]");

        var outputPath = settings.OutputPath ?? Directory.GetCurrentDirectory();
        var isJson = projectPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        // MSIX is a separate output format with its own compiler path. JSON input
        // cannot produce MSIX today; script input would need Installer.BuildMsix()
        // inside the script, which BuildCommand does not invoke on its behalf.
        if (string.Equals(settings.Format, "msix", StringComparison.OrdinalIgnoreCase))
        {
            if (isJson)
            {
                _console.WriteError("MSIX packages cannot be built from JSON configuration. Use the C# script API with InstallerMsix.BuildMsix() / InstallerMsix.BuildMsixBundle() instead (see FalkForge.Compiler.Msix.InstallerMsix).");
                return ExitCodes.ValidationFailure;
            }

            if (!OperatingSystem.IsWindows())
            {
                _console.WriteError("MSIX compilation requires Windows.");
                return ExitCodes.RuntimeError;
            }

            _console.MarkupLine("[yellow]MSIX CLI build is experimental and not yet implemented.[/]");
            _console.MarkupLine("[grey]For MSIX/MSIX-bundle output, call InstallerMsix.BuildMsix() / InstallerMsix.BuildMsixBundle() directly from a .csx build script (see FalkForge.Compiler.Msix.InstallerMsix). Falling back to MSI build.[/]");
        }

        var loadResult = BuildInputResolver.Load(projectPath);
        if (loadResult.IsFailure)
        {
            _console.WriteError(loadResult.Error.Message);
            return ExitCodes.FromErrorKind(loadResult.Error.Kind);
        }

        var package = loadResult.Value;
        if (isJson)
            _console.MarkupLine($"[green]Loaded JSON config:[/] {Markup.Escape(package.Name)} v{package.Version}");

        if (!OperatingSystem.IsWindows())
        {
            _console.WriteError("MSI compilation requires Windows.");
            return ExitCodes.RuntimeError;
        }

        var compileResult = CompilePackage(package, outputPath);
        if (compileResult.IsFailure)
        {
            _console.WriteError(compileResult.Error.Message);
            return ExitCodes.FromErrorKind(compileResult.Error.Kind);
        }

        _console.MarkupLine($"[green]Build succeeded:[/] {Markup.Escape(compileResult.Value)}");

        if (settings.GenerateWinGet)
        {
            var wingetResult = GenerateWinGetManifest(compileResult.Value, package, settings);
            if (wingetResult.IsFailure)
                _console.MarkupLine($"[yellow]Warning:[/] WinGet manifest generation failed: {Markup.Escape(wingetResult.Error.Message)}");
            else
                _console.MarkupLine("[green]WinGet manifest written alongside installer[/]");
        }

        return ExitCodes.Success;
    }

    [SupportedOSPlatform("windows")]
    private static Result<string> CompilePackage(PackageModel package, string outputPath)
    {
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        var compiler = new MsiCompiler();
        return compiler.Compile(package, outputPath);
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
