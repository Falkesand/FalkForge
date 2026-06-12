namespace FalkForge.Cli;

using System.Diagnostics;
using System.Text;

/// <summary>
/// Production implementation of <see cref="IEngineLauncher"/>.
/// Starts the engine as a child process, captures stdout, and returns when it exits.
/// </summary>
internal sealed class DefaultEngineLauncher : IEngineLauncher
{
    /// <inheritdoc/>
    public async Task<EngineLaunchResult> LaunchAsync(
        string exePath, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            Arguments = string.Join(" ", args.Select(EscapeArg)),
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdoutBuilder = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdoutBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return new EngineLaunchResult(process.ExitCode, stdoutBuilder.ToString());
    }

    /// <summary>
    /// Wraps an argument in double quotes and escapes internal double quotes.
    /// Uses the standard Windows escaping convention (backslash before quote).
    /// </summary>
    private static string EscapeArg(string arg)
    {
        // If arg has no special characters, pass as-is
        if (arg.Length > 0 && !arg.Contains(' ') && !arg.Contains('"'))
            return arg;

        // Wrap in quotes, escaping embedded quotes
        return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
