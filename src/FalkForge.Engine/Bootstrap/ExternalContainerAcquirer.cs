namespace FalkForge.Engine.Bootstrap;

using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Runtime side of A6: for every <see cref="ExternalContainerInfo"/> a bundle declares, downloads the
/// container file, verifies it, binds its payloads to the bundle's signed set, and extracts them into
/// the same cache directory the resolved-path install chain (#56) reads from — so an external-container
/// payload installs through exactly the same path an embedded payload does.
///
/// <para><b>Integrity (never trust an unverified container).</b> Three layers, in order, before a byte
/// is installed:
/// <list type="number">
///   <item><description><b>Whole-file hash.</b> The download seam verifies the downloaded bytes against
///   the manifest's declared container <see cref="ExternalContainerInfo.Sha256"/> and fails loud on any
///   mismatch — a container that does not hash to the declared value is discarded, never opened.</description></item>
///   <item><description><b>Declared membership.</b> The container's own table of contents must carry
///   exactly the package ids the manifest declared for it — a container that drops or adds a payload
///   relative to the manifest is rejected.</description></item>
///   <item><description><b>Signed-set binding.</b> The injected trust check binds each container payload
///   to the bundle's ECDSA-signed manifest set. For a signed bundle this is the authoritative gate: a
///   re-hosted or rebuilt container whose payloads were not signed by the trusted publisher is rejected
///   before extraction, even if its whole-file hash matches an attacker-rewritten manifest field. An
///   unsigned bundle passes this layer through, retaining the whole-file + per-payload tamper detection
///   an unsigned embedded bundle has.</description></item>
/// </list>
/// Only then are the payloads streamed out via <see cref="BundleReader.ExtractPayloadToFile(string, TocEntry, string, string)"/>,
/// which verifies each payload's own SHA-256 in the same pass. Any failure aborts the whole acquisition
/// — no partial, unverified install proceeds.</para>
///
/// <para>The HTTP download itself is injected (<see cref="DownloadDelegate"/>) so the production path
/// reuses the engine's existing verified <c>PayloadDownloader</c> while this component's verify → bind →
/// extract logic is exercised in tests with a faithful in-process download seam. The genuine live network
/// download is only reachable with a real URL and network, so it is exercised end-to-end against a real
/// host rather than in unit tests.</para>
/// </summary>
internal sealed class ExternalContainerAcquirer
{
    /// <summary>
    /// Downloads the resource at <paramref name="url"/> to <paramref name="targetPath"/>, verifying the
    /// finished file's SHA-256 equals <paramref name="expectedSha256"/> and returning a failure Result on
    /// any mismatch or transport error. Matches the verified contract of the engine's
    /// <c>PayloadDownloader.DownloadAsync</c>.
    /// </summary>
    internal delegate Task<Result<string>> DownloadDelegate(
        string url, string expectedSha256, string targetPath, CancellationToken ct);

    private readonly DownloadDelegate _download;
    private readonly Func<InstallerManifest, IReadOnlyList<TocEntry>, Result<Unit>> _verifyContainerTrust;

    /// <param name="download">Verified download seam (production: the engine's PayloadDownloader).</param>
    /// <param name="verifyContainerTrust">
    /// Binds a container's TOC entries to the bundle's signed manifest set (production:
    /// <c>BundleTrustGate.Verify</c> closed over the run's require-signed flag + trust state). Returns
    /// success for an unsigned bundle (pass-through) and a failure for any signed-set violation.
    /// </param>
    internal ExternalContainerAcquirer(
        DownloadDelegate download,
        Func<InstallerManifest, IReadOnlyList<TocEntry>, Result<Unit>> verifyContainerTrust)
    {
        _download = download;
        _verifyContainerTrust = verifyContainerTrust;
    }

    /// <summary>
    /// Acquires every external container declared by <paramref name="manifest"/>, extracting each
    /// member payload to <c>{cacheDir}/{PackageId}</c>. No-op (immediate success) when the manifest
    /// declares no external containers.
    /// </summary>
    internal async Task<Result<Unit>> AcquireAllAsync(
        InstallerManifest manifest, string cacheDir, CancellationToken ct)
    {
        if (manifest.ExternalContainers.Length == 0)
            return Unit.Value;

        string containersDir;
        try
        {
            containersDir = Path.Combine(cacheDir, "containers");
            Directory.CreateDirectory(containersDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.DownloadError,
                $"Failed to prepare external container cache directory: {ex.Message}");
        }

        foreach (var info in manifest.ExternalContainers)
        {
            var result = await AcquireOneAsync(manifest, info, containersDir, cacheDir, ct).ConfigureAwait(false);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    private async Task<Result<Unit>> AcquireOneAsync(
        InstallerManifest manifest, ExternalContainerInfo info, string containersDir, string cacheDir,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(info.DownloadUrl) || string.IsNullOrWhiteSpace(info.Sha256))
            return Result<Unit>.Failure(ErrorKind.DownloadError,
                $"External container '{info.Id}' is missing a download URL or hash — refusing to acquire it.");

        // Local file name is derived from the (hex, injection-safe) container hash, never from the
        // attacker-influenceable manifest file name, so the download target can never escape the cache.
        var containerPath = Path.Combine(containersDir, $"{info.Sha256}.ffcontainer");

        // Layer 1 — whole-file hash. The seam verifies the downloaded bytes against the declared hash and
        // fails loud on mismatch; an unverified container is never written to containerPath / never opened.
        var downloadResult = await _download(info.DownloadUrl, info.Sha256, containerPath, ct).ConfigureAwait(false);
        if (downloadResult.IsFailure)
            return Result<Unit>.Failure(ErrorKind.DownloadError,
                $"External container '{info.Id}' could not be downloaded/verified: {downloadResult.Error.Message}");

        var verifiedPath = downloadResult.Value;

        var contentResult = BundleReader.Extract(verifiedPath);
        if (contentResult.IsFailure)
            return Result<Unit>.Failure(ErrorKind.PayloadError,
                $"External container '{info.Id}' is not a readable container: {contentResult.Error.Message}");

        var tocEntries = contentResult.Value.TocEntries;

        // Layer 2 — declared membership. The container must deliver exactly the payloads the manifest
        // promised for it: no dropped payload (would break the install), no extra payload (would be an
        // unsigned smuggled-in binary the signed-set check below also rejects, but fail earlier + clearer).
        var membership = VerifyMembership(info, tocEntries);
        if (membership.IsFailure)
            return membership;

        // Layer 3 — signed-set binding (authoritative for signed bundles; pass-through for unsigned).
        // Runs BEFORE any byte is extracted so a payload not covered by the trusted signature never
        // reaches disk.
        var trust = _verifyContainerTrust(manifest, tocEntries);
        if (trust.IsFailure)
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                $"External container '{info.Id}' failed signed-set verification: {trust.Error.Message}");

        foreach (var entry in tocEntries)
        {
            // External container payloads are full payloads by construction (the compiler never writes a
            // delta into an external container). A delta entry here has no base to reconstruct against on
            // this path, so treat it as a malformed/crafted container rather than write a raw delta blob.
            if (entry.IsDelta)
                return Result<Unit>.Failure(ErrorKind.PayloadError,
                    $"External container '{info.Id}' payload '{entry.PackageId}' is a delta payload, which is " +
                    "not supported in an external container — refusing to install.");

            // Stream-decompress + per-payload SHA-256 verify straight to {cacheDir}/{PackageId}, through
            // the containment choke point (rejects a crafted PackageId that would escape the cache).
            var extractResult = BundleReader.ExtractPayloadToFile(verifiedPath, entry, cacheDir, entry.PackageId);
            if (extractResult.IsFailure)
                return Result<Unit>.Failure(ErrorKind.PayloadError,
                    $"External container '{info.Id}' payload '{entry.PackageId}' failed extraction/verification: " +
                    $"{extractResult.Error.Message}");
        }

        return Unit.Value;
    }

    private static Result<Unit> VerifyMembership(ExternalContainerInfo info, IReadOnlyList<TocEntry> tocEntries)
    {
        var declared = new HashSet<string>(info.PackageIds, StringComparer.Ordinal);
        var actual = new HashSet<string>(tocEntries.Count, StringComparer.Ordinal);
        foreach (var entry in tocEntries)
            actual.Add(entry.PackageId);

        if (!declared.SetEquals(actual))
            return Result<Unit>.Failure(ErrorKind.PayloadError,
                $"External container '{info.Id}' delivered payloads [{string.Join(", ", actual)}] but the manifest " +
                $"declared [{string.Join(", ", declared)}] — refusing to install a container whose contents do not " +
                "match the manifest.");

        return Unit.Value;
    }
}
