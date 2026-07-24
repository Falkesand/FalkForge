namespace FalkForge.Engine;

using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Resolves the extracted elevation companion for the self-extract bootstrapper and proves it may
/// be wired for elevated execution. Extracted (like <see cref="Bootstrapper"/>) from
/// <c>BootstrapperRunner.RunAsync</c> so the decision is unit-testable without processes or pipes.
///
/// <para><b>The trust chain this completes.</b> Before this runs, the bootstrapper has already
/// (1) bound the overlay TOC hashes to the ECDSA-signed manifest for signed bundles
/// (<c>BundleTrustGate</c> — the companion is inside the signed set like every payload), and
/// (2) streamed each payload to the cache while verifying its bytes against its TOC hash
/// (<see cref="BundleReader"/> — a tampered companion never lands on disk). This resolver adds the
/// final link: the TOC hash the extractor trusted must equal the hash the manifest DECLARES for
/// the companion (<see cref="InstallerManifest.EngineCompanionSha256"/>), so bytes == TOC ==
/// declared (== signed, when a signature is present). Only then is the extracted path handed to
/// the elevation gateway.</para>
///
/// <para><b>Fail-closed rules.</b> A manifest that declares no companion never wires one — even
/// when the TOC smuggles a payload under the reserved id (an undeclared SYSTEM binary must not
/// run; the engine falls back to per-user). A declared companion that is absent, whose hash
/// disagrees, or whose extracted file is missing is a <see cref="ErrorKind.SecurityError"/> — the
/// caller aborts rather than installing from a bundle whose elevation binary cannot be trusted.</para>
/// </summary>
internal static class BootstrapCompanionResolver
{
    /// <summary>
    /// Resolves and verifies the extracted companion under <paramref name="cacheDir"/>.
    /// Returns <see cref="BootstrapCompanionResolution.None"/> when the manifest declares no
    /// companion, the verified path on success, and a <see cref="ErrorKind.SecurityError"/>
    /// failure when the declared companion cannot be trusted.
    /// </summary>
    internal static Result<BootstrapCompanionResolution> Resolve(
        InstallerManifest manifest,
        IReadOnlyList<TocEntry> tocEntries,
        string cacheDir)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tocEntries);

        // No declaration → no companion is ever wired. A TOC payload under the reserved id with
        // no manifest declaration stays an inert cache file; it is never executed for elevation.
        if (string.IsNullOrEmpty(manifest.EngineCompanionSha256))
            return Result<BootstrapCompanionResolution>.Success(BootstrapCompanionResolution.None);

        TocEntry? companionEntry = null;
        foreach (var entry in tocEntries)
        {
            if (string.Equals(entry.PackageId, EngineCompanionPayload.PackageId, StringComparison.Ordinal))
            {
                companionEntry = entry;
                break;
            }
        }

        if (companionEntry is null)
            return Result<BootstrapCompanionResolution>.Failure(ErrorKind.SecurityError,
                $"The manifest declares an elevation companion ({EngineCompanionPayload.PackageId}) " +
                "but the bundle carries no such payload — corrupt or tampered bundle. Refusing to " +
                "install; elevation cannot be established from this bundle.");

        // The value the extractor verified the companion's bytes against: the TOC hash for a full
        // payload, the reconstructed-file hash for a delta payload (the delta-blob hash is
        // irrelevant to trust — same rule as SignedPayloadTocVerifier).
        var boundHash = companionEntry.IsDelta
            ? companionEntry.ReconstructedSha256Hash
            : companionEntry.Sha256Hash;

        if (string.IsNullOrEmpty(boundHash)
            || !string.Equals(boundHash, manifest.EngineCompanionSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Result<BootstrapCompanionResolution>.Failure(ErrorKind.SecurityError,
                $"The elevation companion payload ({EngineCompanionPayload.PackageId}) does not " +
                $"match the manifest's declared hash ({manifest.EngineCompanionSha256}); the bundle " +
                $"carries {boundHash ?? "<none>"}. The companion executes elevated, so a hash " +
                "disagreement is treated as tampering — refusing to install.");
        }

        // The bootstrapper's extraction loop wrote the companion to <cacheDir>\<PackageId> after
        // verifying its bytes against the (now manifest-bound) TOC hash. A missing file here means
        // extraction did not actually produce the companion — never continue as if it had.
        var extractedPath = Path.Combine(cacheDir, EngineCompanionPayload.PackageId);
        if (!File.Exists(extractedPath))
            return Result<BootstrapCompanionResolution>.Failure(ErrorKind.SecurityError,
                $"The verified elevation companion was not found at its extraction path " +
                $"({extractedPath}). Refusing to install; elevation cannot be established.");

        return Result<BootstrapCompanionResolution>.Success(new BootstrapCompanionResolution(extractedPath));
    }
}
