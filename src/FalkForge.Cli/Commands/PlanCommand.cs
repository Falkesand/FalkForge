using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FalkForge.Cli.Settings;
using FalkForge.Engine.Protocol.Bundle;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Runs the installer engine through detection and planning, then outputs the install plan
/// without performing any installation. Accepts a compiled bundle EXE as input, extracts its
/// embedded manifest, launches the engine in headless plan-only mode, and renders the result.
/// </summary>
[Description("Run the installer pipeline through planning and output the plan without installing")]
internal sealed class PlanCommand : Command<PlanSettings>
{
    private readonly IConsoleOutput _output;
    private readonly System.IO.TextWriter _jsonSink;
    private readonly IEngineLauncher _launcher;

    public PlanCommand() : this(new SpectreConsoleOutput()) { }

    public PlanCommand(
        IConsoleOutput output,
        System.IO.TextWriter? jsonSink = null,
        IEngineLauncher? launcher = null)
    {
        _output = output;
        _jsonSink = jsonSink ?? Console.Out;
        _launcher = launcher ?? new DefaultEngineLauncher();
    }

    protected override int Execute(
        [NotNull] CommandContext context,
        [NotNull] PlanSettings settings,
        CancellationToken cancellationToken)
    {
        var jsonOutput = settings.Json ? new JsonConsoleOutput() : null;
        var output = (IConsoleOutput?)jsonOutput ?? _output;

        var exitCode = ExecuteCore(settings, output, cancellationToken);

        if (jsonOutput is not null)
            _jsonSink.WriteLine(jsonOutput.WriteEnvelope("plan", exitCode));

        return exitCode;
    }

    private int ExecuteCore(
        PlanSettings settings,
        IConsoleOutput output,
        CancellationToken ct)
    {
        var bundlePath = Path.GetFullPath(settings.ProjectPath);

        if (!File.Exists(bundlePath))
        {
            output.WriteError($"File not found: {bundlePath}");
            return ExitCodes.RuntimeError;
        }

        // Extract the embedded manifest from the bundle EXE.
        var extractResult = BundleReader.Extract(bundlePath);
        if (extractResult.IsFailure)
        {
            output.WriteError($"Failed to read bundle: {extractResult.Error.Message}");
            return ExitCodes.RuntimeError;
        }

        var content = extractResult.Value;
        if (content.ManifestJsonBytes is null || content.ManifestJsonBytes.Length == 0)
        {
            output.WriteError("Bundle does not contain an embedded manifest.");
            return ExitCodes.RuntimeError;
        }

        // Write manifest to a temp file for the engine subprocess to load.
        var tempDir = Path.Combine(
            Path.GetTempPath(), "FalkForge", $"plan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            return ExecuteWithTempDir(settings, output, tempDir, content.ManifestJsonBytes, ct);
        }
        finally
        {
            // Best-effort cleanup of the per-run scratch directory so repeated invocations do not
            // accumulate orphaned temp directories. Swallow IO failures (e.g. a file still held by
            // a slow-exiting engine) — a cleanup failure must not turn a successful plan into an error.
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private int ExecuteWithTempDir(
        PlanSettings settings,
        IConsoleOutput output,
        string tempDir,
        byte[] manifestJsonBytes,
        CancellationToken ct)
    {
        var manifestPath = Path.Combine(tempDir, "manifest.json");
        var planOutputPath = settings.OutputPath ?? Path.Combine(tempDir, "plan.json");

        try
        {
            File.WriteAllBytes(manifestPath, manifestJsonBytes);
        }
        catch (Exception ex)
        {
            output.WriteError($"Failed to write temp manifest: {ex.Message}");
            return ExitCodes.RuntimeError;
        }

        // Locate the engine binary alongside the CLI binary.
        var enginePath = FindEngineBinary();
        if (enginePath is null)
        {
            output.WriteError(
                "Engine binary 'FalkForge.Engine.exe' not found. " +
                "Ensure the engine is built and placed in the same directory as the forge CLI.");
            return ExitCodes.RuntimeError;
        }

        // Launch the engine in headless plan-only mode.
        var engineArgs = BuildEngineArgs(manifestPath, planOutputPath);

        EngineLaunchResult launchResult;
        try
        {
            launchResult = _launcher.LaunchAsync(enginePath, engineArgs, ct)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            output.WriteError("Plan operation was cancelled.");
            return ExitCodes.RuntimeError;
        }
        catch (Exception ex)
        {
            output.WriteError($"Failed to launch engine: {ex.Message}");
            return ExitCodes.RuntimeError;
        }

        if (launchResult.ExitCode != 0)
        {
            output.WriteError($"Engine exited with code {launchResult.ExitCode}.");
            if (!string.IsNullOrWhiteSpace(launchResult.Stdout))
                output.WriteError(launchResult.Stdout.Trim());
            // Surface stderr so crash output and engine validation errors are visible.
            if (!string.IsNullOrWhiteSpace(launchResult.Stderr))
                output.WriteError(launchResult.Stderr.Trim());
            return ExitCodes.RuntimeError;
        }

        // Read the plan JSON written by the engine.
        if (!File.Exists(planOutputPath))
        {
            output.WriteError("Engine did not produce a plan file.");
            return ExitCodes.RuntimeError;
        }

        string planJson;
        try
        {
            planJson = File.ReadAllText(planOutputPath);
        }
        catch (Exception ex)
        {
            output.WriteError($"Failed to read plan file: {ex.Message}");
            return ExitCodes.RuntimeError;
        }

        // Render a human-readable summary to the console.
        RenderPlan(planJson, output);

        return ExitCodes.Success;
    }

    /// <summary>
    /// Renders the plan JSON to the console as a readable package action summary.
    /// Silently skips detailed rendering when the JSON cannot be parsed — the file is
    /// still available at the output path for programmatic consumption.
    /// </summary>
    private static void RenderPlan(string planJson, IConsoleOutput output)
    {
        try
        {
            using var doc = JsonDocument.Parse(planJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("packages", out var packages))
            {
                var count = packages.GetArrayLength();
                output.MarkupLine($"[green]Plan produced:[/] {count} package action(s)");

                foreach (var pkg in packages.EnumerateArray())
                {
                    var id = pkg.TryGetProperty("packageId", out var idProp)
                        ? idProp.GetString() ?? "(unknown)"
                        : "(unknown)";
                    var action = pkg.TryGetProperty("action", out var actProp)
                        ? actProp.GetString() ?? "Unknown"
                        : "Unknown";

                    output.MarkupLine($"  [grey]{Markup.Escape(id)}[/]  {Markup.Escape(action)}");
                }
            }
            else
            {
                output.MarkupLine("[green]Plan produced.[/]");
            }
        }
        catch
        {
            // Non-fatal: JSON may be available at the output path for direct inspection.
            output.MarkupLine("[green]Plan produced.[/]");
        }
    }

    /// <summary>
    /// Locates the engine binary alongside the running CLI binary.
    /// Returns <c>null</c> when the engine cannot be found.
    /// </summary>
    internal static string? FindEngineBinary()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "FalkForge.Engine.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Builds the argument list to pass to the engine process for plan-only mode.
    /// </summary>
    internal static string[] BuildEngineArgs(string manifestPath, string? outputPath)
    {
        var args = new List<string>
        {
            "--manifest", manifestPath,
            "--plan-only"
        };

        if (outputPath is not null)
        {
            args.Add("--plan-output");
            args.Add(outputPath);
        }

        return [.. args];
    }
}
