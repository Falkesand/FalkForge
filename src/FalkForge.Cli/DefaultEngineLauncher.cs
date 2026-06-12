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
    /// Quotes an argument per the Windows <c>CommandLineToArgvW</c> rules so the child engine
    /// process receives it intact. A backslash is special ONLY when it precedes a double-quote
    /// (or the closing quote of a quoted argument); interior backslashes — including every
    /// backslash in a normal Windows path — must stay single. The previous implementation
    /// doubled all backslashes, corrupting paths such as <c>C:\Users\John Doe\manifest.json</c>.
    /// </summary>
    internal static string EscapeArg(string arg)
    {
        // No spaces, tabs or quotes: the argument needs no quoting at all (and must not be
        // mangled). This is the common case for plain Windows paths without spaces.
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
            return arg;

        var sb = new StringBuilder(arg.Length + 2);
        sb.Append('"');

        var i = 0;
        while (i < arg.Length)
        {
            // Count the run of backslashes starting at i.
            var backslashes = 0;
            while (i < arg.Length && arg[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == arg.Length)
            {
                // Trailing backslashes precede the closing quote: double them so the quote is
                // not escaped, but they are not interpreted as part of an escape sequence.
                sb.Append('\\', backslashes * 2);
            }
            else if (arg[i] == '"')
            {
                // Backslashes before a literal quote are doubled, then the quote is escaped.
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                i++;
            }
            else
            {
                // Backslashes not followed by a quote are emitted verbatim (single).
                sb.Append('\\', backslashes);
                sb.Append(arg[i]);
                i++;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
