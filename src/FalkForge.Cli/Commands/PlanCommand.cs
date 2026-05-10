using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Runs the installer pipeline through detection and planning, outputs the install plan
/// as JSON, and exits without installing anything.
/// </summary>
/// <remarks>
/// Not registered in the production CLI until the engine supports --plan-only mode.
/// TODO: re-enable when plan-only engine mode lands.
/// </remarks>
[Description("Run the installer pipeline through planning and output the plan without installing")]
internal sealed class PlanCommand : Command<PlanSettings>
{
    private readonly IConsoleOutput _output;
    private readonly System.IO.TextWriter _jsonSink;

    public PlanCommand() : this(new SpectreConsoleOutput()) { }

    public PlanCommand(IConsoleOutput output, System.IO.TextWriter? jsonSink = null)
    {
        _output = output;
        _jsonSink = jsonSink ?? Console.Out;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] PlanSettings settings, CancellationToken cancellationToken)
    {
        var jsonOutput = settings.Json ? new JsonConsoleOutput() : null;
        var output = (IConsoleOutput?)jsonOutput ?? _output;

        var exitCode = ExecuteCore(settings, output);

        if (jsonOutput is not null)
            _jsonSink.WriteLine(jsonOutput.WriteEnvelope("plan", exitCode));

        return exitCode;
    }

    private static int ExecuteCore(PlanSettings settings, IConsoleOutput output)
    {
        var projectPath = Path.GetFullPath(settings.ProjectPath);

        if (!File.Exists(projectPath))
        {
            output.WriteError($"File not found: {projectPath}");
            return ExitCodes.RuntimeError;
        }

        // Full engine integration requires the engine binary built with NativeAOT.
        // The engine is launched as a subprocess, passing --plan-only so it outputs the plan
        // JSON and exits without installing. Return an error until the binary is available.
        output.WriteError("The 'forge plan' command requires the engine binary to be compiled first.");
        output.WriteError($"Project: {Markup.Escape(settings.ProjectPath)}");

        return ExitCodes.RuntimeError;
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
