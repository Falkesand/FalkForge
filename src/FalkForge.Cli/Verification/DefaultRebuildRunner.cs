using System.Diagnostics;
using System.Text;

namespace FalkForge.Cli.Verification;

/// <summary>
/// Production <see cref="IRebuildRunner"/>. Invokes <c>dotnet run --project &lt;proj&gt; -- -o &lt;dir&gt;</c>
/// with <c>SOURCE_DATE_EPOCH</c> set, captures stdout/stderr, and enforces a timeout. This is the
/// same invocation shape the integration-test demo fixture uses, so a project that calls
/// <c>Reproducible()</c> emits a deterministic artifact into the scratch directory.
/// </summary>
internal sealed class DefaultRebuildRunner : IRebuildRunner
{
    /// <inheritdoc/>
    public async Task<RebuildResult> RebuildAsync(
        string projectPath,
        string outputDir,
        long sourceDateEpoch,
        TimeSpan timeout,
        CancellationToken ct)
    {
#pragma warning disable S4036 // PATH lookup is the platform contract for the dotnet host (install location varies)
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Directory.GetCurrentDirectory(),
        };
#pragma warning restore S4036

        foreach (var arg in BuildArguments(projectPath, outputDir))
            psi.ArgumentList.Add(arg);

        // Reproducible builds key their content-digest PackageCode and MSI/bundle timestamps off
        // SOURCE_DATE_EPOCH. Pinning it is what makes two independent builds byte-identical.
        psi.Environment["SOURCE_DATE_EPOCH"] = sourceDateEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Disable MSBuild node reuse so worker nodes do not linger and pollute later builds.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Timed out or caller cancelled. Kill the whole tree so dotnet/MSBuild children do not
            // keep holding the scratch directory after this method returns.
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }

            if (ct.IsCancellationRequested)
                throw;

            return new RebuildResult(
                ExitCode: -1,
                Stdout: stdout.ToString(),
                Stderr: $"Rebuild timed out after {timeout.TotalSeconds:N0}s.\n{stderr}");
        }

        return new RebuildResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Builds the <c>dotnet</c> argument list for a reproducible rebuild of
    /// <paramref name="projectPath"/> into <paramref name="outputDir"/>.
    /// </summary>
    internal static string[] BuildArguments(string projectPath, string outputDir) =>
        ["run", "--project", projectPath, "--", "-o", outputDir];
}
