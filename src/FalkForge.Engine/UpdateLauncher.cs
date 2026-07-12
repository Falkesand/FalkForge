using System.Diagnostics;
using FalkForge.Platform.Windows;

namespace FalkForge.Engine;

internal sealed class DefaultUpdateLauncher : IUpdateLauncher
{
    private readonly string? _cacheRoot;
    private readonly IAuthenticodeValidator? _authenticodeValidator;
    private readonly string? _expectedThumbprint;

    /// <summary>
    /// Initializes the launcher.
    /// </summary>
    /// <param name="cacheRoot">
    /// When non-null, the update path must reside inside this directory tree
    /// (path-containment check prevents directory-traversal attacks).
    /// </param>
    /// <param name="authenticodeValidator">
    /// When non-null, Authenticode signature is verified before the process is started.
    /// Pass <c>null</c> only in environments where signature verification is not available
    /// (e.g. non-Windows test hosts that cannot sign binaries).
    /// </param>
    /// <param name="expectedThumbprint">
    /// Optional publisher thumbprint pinned in the installer manifest
    /// (<c>InstallerManifest.UpdatePublisherThumbprint</c>). When non-null, the certificate
    /// thumbprint on the downloaded bundle must match this value exactly; a mismatch is
    /// treated as a security error and the launch is aborted.
    /// </param>
    internal DefaultUpdateLauncher(
        string? cacheRoot = null,
        IAuthenticodeValidator? authenticodeValidator = null,
        string? expectedThumbprint = null)
    {
        _cacheRoot = cacheRoot;
        _authenticodeValidator = authenticodeValidator;
        _expectedThumbprint = expectedThumbprint;
    }

    public Result<Unit> Launch(string updatePath)
    {
        // Path containment check — prevents directory-traversal from escaping the cache.
        if (_cacheRoot is not null)
        {
            var fullUpdate = Path.GetFullPath(updatePath);
            var fullCache = Path.GetFullPath(_cacheRoot);
            // Ensure cache path ends with separator to prevent prefix attacks
            // (e.g. /tmp/cache matching /tmp/cachemalicious)
            if (!fullCache.EndsWith(Path.DirectorySeparatorChar))
                fullCache += Path.DirectorySeparatorChar;
            if (!fullUpdate.StartsWith(fullCache, StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(new Error(ErrorKind.SecurityError,
                    "UPD005: Update path is outside the cache root."));
        }

        if (!File.Exists(updatePath))
            return Result<Unit>.Failure(new Error(ErrorKind.EngineError,
                $"UPD005: Update file not found: '{Path.GetFileName(updatePath)}'."));

        // Authenticode verification — must precede Process.Start.
        // Security rationale: a MITM-replaced or cache-poisoned bundle EXE must never
        // execute. This is the last code-integrity gate before arbitrary code runs on the
        // user's machine (CVE class: CWE-494 Download of Code Without Integrity Check).
        if (_authenticodeValidator is not null)
        {
            var sigResult = _authenticodeValidator.ValidateSignature(updatePath, _expectedThumbprint, expectedPublicKeyHash: null);
            if (sigResult.IsFailure)
                return Result<Unit>.Failure(new Error(ErrorKind.SecurityError,
                    $"UPD006: Update bundle rejected — signature invalid: {sigResult.Error.Message}"));
        }

        try
        {
            var startInfo = new ProcessStartInfo(updatePath) { UseShellExecute = true };
            foreach (var arg in BuildRelaunchArguments(Environment.ProcessPath))
                startInfo.ArgumentList.Add(arg);

            using var process = Process.Start(startInfo);
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(new Error(ErrorKind.EngineError,
                $"UPD005: Failed to launch update: {ex.Message}"));
        }
    }

    /// <summary>
    /// Builds the command-line arguments the already-installed, already-trusted engine passes when it
    /// relaunches a downloaded update bundle (C14 Stage 2, closes B2 on the update path).
    ///
    /// <para><c>--require-signed</c> is always asserted: the trust decision is made by the currently
    /// running engine (which the user already trusts), not by the downloaded artifact. The relaunched
    /// bundle's integrity gate then rejects a stripped/unsigned or untrusted-signed update (INT007 /
    /// INT001) before extracting or executing a single payload. A fresh install of an unsigned bundle
    /// stays allowed-but-warned because it never goes through this launcher.</para>
    ///
    /// <para>When <paramref name="basePath"/> is a real path it is forwarded as <c>--base-bundle</c> so a
    /// delta update can reconstruct its payloads against the currently-installed bundle; a full update
    /// bundle simply ignores it. Each token is emitted separately so <see cref="ProcessStartInfo.ArgumentList"/>
    /// quotes a base path containing spaces intact.</para>
    /// </summary>
    internal static List<string> BuildRelaunchArguments(string? basePath)
    {
        // The update path is always require-signed, independent of whether a delta base is available.
        var args = new List<string> { "--require-signed" };

        if (!string.IsNullOrEmpty(basePath))
        {
            args.Add("--base-bundle");
            args.Add(basePath);
        }

        return args;
    }
}
