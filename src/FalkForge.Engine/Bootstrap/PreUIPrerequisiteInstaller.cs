namespace FalkForge.Engine.Bootstrap;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Runs missing pre-UI prerequisites sequentially before the managed WPF UI process launches.
/// Each prerequisite is dispatched to a child process via <see cref="IProcessRunner"/>.
/// </summary>
/// <remarks>
/// <para>
/// Cancellation contract: this class depends on contract (b) — the runner's
/// <see cref="IProcessRunner.RunAsync(string,string,Action{int}?,CancellationToken)"/>
/// overload accepts a <see cref="CancellationToken"/> and throws
/// <see cref="OperationCanceledException"/> when the token fires. After catching
/// that exception, <see cref="IProcessRunner.KillTree"/> is called with the PID
/// that was reported via the <c>onProcessStarted</c> callback, so that the child
/// process tree is terminated even if the OS did not deliver SIGTERM.
/// </para>
/// <para>
/// Exit code semantics:
/// <list type="bullet">
///   <item>0 — success, continue.</item>
///   <item>3010 — soft reboot required; behaviour governed by <see cref="PreUIRebootBehavior"/>.</item>
///   <item>1641 — forced reboot; always returns <see cref="PreUIResult.RebootRequired"/> regardless of behaviour.</item>
///   <item>Any other non-zero — returns <see cref="PreUIResult.Failed"/> immediately.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class PreUIPrerequisiteInstaller
{
    private const int ExitCodeSoftReboot = 3010;
    private const int ExitCodeForcedReboot = 1641;
    private const string Category = nameof(PreUIPrerequisiteInstaller);

    private readonly IProcessRunner _runner;
    private readonly string _extractionDir;
    private readonly IEngineLogger? _logger;

    /// <summary>
    /// Creates a new <see cref="PreUIPrerequisiteInstaller"/>.
    /// </summary>
    /// <param name="runner">Process runner used to launch each prerequisite.</param>
    /// <param name="extractionDir">
    /// Root of the cache directory. Prerequisite payloads are expected at
    /// <c>{extractionDir}/preui/{pkg.SourcePath}</c>.
    /// </param>
    /// <param name="logger">Optional engine logger for warnings and diagnostics.</param>
    public PreUIPrerequisiteInstaller(
        IProcessRunner runner,
        string extractionDir,
        IEngineLogger? logger = null)
    {
        _runner = runner;
        _extractionDir = extractionDir;
        _logger = logger;
    }

    /// <summary>
    /// Runs all <paramref name="missing"/> packages sequentially.
    /// Returns a <see cref="PreUIResult"/> discriminated union describing the outcome.
    /// </summary>
    /// <param name="missing">Ordered list of prerequisites to install.</param>
    /// <param name="progress">Progress sink updated as each package is processed.</param>
    /// <param name="cancellationToken">Token used to cancel the run mid-package.</param>
    public async Task<PreUIResult> RunAllAsync(
        IReadOnlyList<PreUIPackageInfo> missing,
        IProgressSink progress,
        CancellationToken cancellationToken)
    {
        int total = missing.Count;

        for (int i = 0; i < total; i++)
        {
            var pkg = missing[i];

            // Update progress before attempting install
            int percentBefore = total == 0 ? 0 : i * 100 / total;
            progress.SetPercent(percentBefore);
            progress.SetMessage($"Installing {pkg.DisplayName}...");

            string exePath = Path.Combine(_extractionDir, "preui", pkg.SourcePath);

            int? capturedPid = null;

            int exitCode;
            try
            {
                exitCode = await _runner.RunAsync(
                    exePath,
                    pkg.Arguments,
                    onProcessStarted: pid => capturedPid = pid,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Kill the child tree before returning so no orphan processes remain.
                if (capturedPid.HasValue)
                    _runner.KillTree(capturedPid.Value);

                return new PreUIResult.Cancelled();
            }

            var outcome = HandleExitCode(pkg, exitCode);
            if (outcome is not null)
                return outcome;
        }

        progress.SetPercent(100);
        return new PreUIResult.Success();
    }

    /// <summary>
    /// Maps an exit code to a terminal <see cref="PreUIResult"/>, or returns
    /// <see langword="null"/> to signal "continue to next package".
    /// </summary>
    private PreUIResult? HandleExitCode(PreUIPackageInfo pkg, int exitCode)
    {
        if (exitCode == 0)
            return null; // success — continue

        if (exitCode == ExitCodeForcedReboot)
        {
            // 1641 = reboot has been initiated; always stop regardless of RebootBehavior.
            _logger?.Warning(Category, $"Package '{pkg.DisplayName}' initiated a forced reboot (exit 1641).");
            return new PreUIResult.RebootRequired(pkg, exitCode);
        }

        if (exitCode == ExitCodeSoftReboot)
        {
            return pkg.RebootBehavior switch
            {
                PreUIRebootBehavior.IgnoreAndContinue =>
                    HandleSoftRebootIgnore(pkg),

                PreUIRebootBehavior.Block =>
                    new PreUIResult.RebootRequired(pkg, exitCode),

                PreUIRebootBehavior.Prompt =>
                    // Prompt UI is wired in rows 21+; treat as Block until TaskDialog is available.
                    new PreUIResult.RebootRequired(pkg, exitCode),

                _ => new PreUIResult.RebootRequired(pkg, exitCode)
            };
        }

        // Any other non-zero exit code is a hard failure — stop immediately.
        _logger?.Error(Category, $"Package '{pkg.DisplayName}' exited with failure code {exitCode}.");
        return new PreUIResult.Failed(pkg, exitCode);
    }

    private PreUIResult? HandleSoftRebootIgnore(PreUIPackageInfo pkg)
    {
        // Reboot is deferred — the runtime is functional before reboot (e.g. .NET Desktop Runtime).
        _logger?.Warning(Category,
            $"Package '{pkg.DisplayName}' exited with 3010 (soft reboot deferred). " +
            "Continuing to next prerequisite.");
        return null; // continue
    }
}
