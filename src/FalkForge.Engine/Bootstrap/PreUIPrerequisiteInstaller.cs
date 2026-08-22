namespace FalkForge.Engine.Bootstrap;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FalkForge.Diagnostics;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Protocol.Integrity;
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
public sealed class PreUIPrerequisiteInstaller : IPreUIPrerequisiteInstaller
{
    private const int ExitCodeSoftReboot = 3010;
    private const int ExitCodeForcedReboot = 1641;
    private const string Category = nameof(PreUIPrerequisiteInstaller);

    private readonly IProcessRunner _runner;
    private readonly string _extractionDir;
    private readonly IFalkLogger? _logger;

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
        IFalkLogger? logger = null)
    {
        _runner = runner;
        _extractionDir = extractionDir;
        _logger = logger;
    }

    // Sentinel exit code used when a SourcePath fails path-traversal validation.
    // Chosen as -1 so it is distinct from every valid Windows exit code (which are
    // unsigned 32-bit values when interpreted as HRESULT, but stored in int).
    private const int ExitCodePathTraversalRejected = -1;

    // Sentinel exit code used when the payload's on-disk bytes do not match the
    // manifest-declared SHA-256 hash, when that hash is malformed, when the payload could not be
    // opened at all, or when the path the open handle resolves to is not one this launcher will
    // execute. Distinct from ExitCodePathTraversalRejected and every valid Windows exit code.
    private const int ExitCodeHashRejected = -2;

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

            // ── Path-traversal guard (three-layer defense) ────────────────────
            // Layer 1: reject obviously unsafe inputs before any Path API call.
            if (!IsSourcePathSafe(pkg.SourcePath))
            {
                _logger?.Error(Category,
                    $"Package '{pkg.DisplayName}' has an unsafe SourcePath: '{pkg.SourcePath}'. " +
                    "Rejecting to prevent path-traversal to arbitrary executables.");
                return new PreUIResult.Failed(pkg, ExitCodePathTraversalRejected);
            }

            // Layer 2 & 3: resolve and verify containment within <cacheDir>/preui/.
            var preuiRoot = Path.GetFullPath(Path.Combine(_extractionDir, "preui"));
            var resolved  = Path.GetFullPath(Path.Combine(preuiRoot, pkg.SourcePath));

            // The resolved path must be strictly under preuiRoot (with trailing separator).
            var containmentBase = preuiRoot + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(containmentBase, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Error(Category,
                    $"Package '{pkg.DisplayName}' SourcePath '{pkg.SourcePath}' resolves outside " +
                    $"the preui directory. Resolved: '{resolved}'. Rejecting.");
                return new PreUIResult.Failed(pkg, ExitCodePathTraversalRejected);
            }

            string exePath = resolved;

            // ── Payload hash binding ───────────────────────────────────────────
            // Containment (above) proves the file sits inside the cache directory. It does
            // not prove the file is the publisher's file: a same-user process could overwrite
            // the cached payload between extraction and this elevated launch (TOCTOU) and have
            // the swapped bytes run elevated. Open the file ourselves, hash it against the
            // manifest-declared value, and hold the handle open for the entire launch below —
            // FileShare.Read denies other processes write/rename/delete for as long as the
            // handle lives, so the bytes the child process image is mapped from are provably
            // the bytes just hashed. Hashing and then closing the handle before launching would
            // leave the same window open in a smaller box.
            var bound = HashBoundFile.Open(exePath, pkg.Sha256Hash);
            if (bound.Status != HashBoundFileStatus.Verified)
            {
                _logger?.Error(Category,
                    $"Package '{pkg.DisplayName}' payload at '{exePath}' " +
                    $"{DescribeBindingFailure(pkg.Sha256Hash, bound)}. Rejecting.");
                return new PreUIResult.Failed(pkg, ExitCodeHashRejected);
            }

            var payloadStream = bound.Stream!;
            var launchPath = bound.ResolvedPath!;

            // The handle pins the file object, not the reparse points in the path that reached
            // it. A same-user attacker needs no privilege to rename the preui directory, drop a
            // junction of the same name in its place, and repoint that junction while the hash
            // runs -- Process.Start would then resolve the string a second time and launch the
            // attacker's file. launchPath comes from the handle, with every reparse point already
            // followed, so the child process is mapped from the bytes that were hashed.
            //
            // A resolved path that leaves the local disk, or that no longer fits what
            // CreateProcessW accepts, fails closed. Falling back to exePath would put the reparse
            // points straight back into the path.
            if (launchPath.StartsWith(@"\\", StringComparison.Ordinal) ||
                launchPath.Length > HashBoundFile.MaxLegacyPathLength)
            {
                await payloadStream.DisposeAsync().ConfigureAwait(false);
                _logger?.Error(Category,
                    $"Package '{pkg.DisplayName}' payload at '{exePath}' resolves to '{launchPath}', " +
                    "which is either on a network path or too long to launch. Rejecting.");
                return new PreUIResult.Failed(pkg, ExitCodeHashRejected);
            }

            int? capturedPid = null;

            int exitCode;
            try
            {
                try
                {
                    exitCode = await _runner.RunAsync(
                        launchPath,
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
            }
            finally
            {
                // ConfigureAwait(false), not a synchronous Dispose(): this method is async, and
                // CA1849 flags Stream.Dispose() as a blocking call inside an async method
                // regardless of whether the stream is read-only.
                await payloadStream.DisposeAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Turns a <see cref="HashBoundFile"/> failure into this class's own log wording, so the
    /// shared helper never has to phrase a message for a caller it knows nothing about.
    /// </summary>
    private static string DescribeBindingFailure(string expectedHashHex, HashBoundFileResult bound)
        => bound.Status switch
        {
            HashBoundFileStatus.MalformedExpectedHash =>
                $"has a malformed Sha256Hash (expected 64 hex chars, got '{expectedHashHex}')",
            HashBoundFileStatus.FileNotFound or HashBoundFileStatus.OpenFailed =>
                $"could not be opened: {bound.Detail}",
            HashBoundFileStatus.ReadFailed =>
                $"could not be read: {bound.Detail}",
            HashBoundFileStatus.HashMismatch =>
                $"hash does not match the manifest-declared value (expected {expectedHashHex}, " +
                $"computed {bound.Detail})",
            HashBoundFileStatus.PathResolutionFailed =>
                "could not be resolved to a real path from its open handle",
            // Verified never reaches here, and a status added later must produce an honest
            // message rather than inherit one that does not describe it.
            _ => "could not be bound to the manifest-declared hash",
        };

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="sourcePath"/> is safe to use as a
    /// relative path component under the <c>preui/</c> subdirectory.
    /// </summary>
    /// <remarks>
    /// This is Layer 1 of the three-layer path-traversal defense (see <see cref="RunAllAsync"/>).
    /// It rejects inputs that no safe path should ever contain before any <see cref="Path"/> API
    /// call can be made:
    /// <list type="bullet">
    ///   <item>Rooted paths: <c>C:\…</c>, <c>/…</c>, <c>\\server\…</c> — bypass containment entirely.</item>
    ///   <item>Colons (non-drive): <c>foo.exe:stream</c> — NTFS alternate data streams.</item>
    ///   <item>Device namespace prefixes: <c>\\?\</c>, <c>\\.\</c> — Win32 extended paths that bypass
    ///         many security checks.</item>
    ///   <item>Empty or whitespace: no valid payload name.</item>
    /// </list>
    /// Layer 2 (<see cref="Path.GetFullPath"/> + containment check) catches obfuscated traversal
    /// sequences that pass Layer 1 (e.g., <c>sub/../../../evil.exe</c>).
    /// </remarks>
    private static bool IsSourcePathSafe(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return false;

        // Reject rooted paths (absolute paths on any drive, UNC paths, Unix-style roots).
        if (Path.IsPathRooted(sourcePath))
            return false;

        // Reject NTFS alternate data streams: any colon after position 0.
        // (Position 0 colon would make it rooted and caught above on Windows, but be explicit.)
        if (sourcePath.Contains(':', StringComparison.Ordinal))
            return false;

        // Reject Win32 device namespace prefixes that bypass path normalisation.
        // These would cause Path.GetFullPath to behave unexpectedly.
        if (sourcePath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ||
            sourcePath.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
