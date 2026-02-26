using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
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
            _console.MarkupLine("[yellow]MSI compilation from JSON is not yet supported.[/]");
            return ExitCodes.Success;
        }

        var loadResult = ScriptLoader.LoadAndBuild(projectPath, outputPath, settings.Configuration);
        if (loadResult.IsFailure)
        {
            _console.WriteError(loadResult.Error.Message);
            return ExitCodes.FromErrorKind(loadResult.Error.Kind);
        }

        _console.MarkupLine($"[green]Build succeeded:[/] {Markup.Escape(loadResult.Value)}");
        return ExitCodes.Success;
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
