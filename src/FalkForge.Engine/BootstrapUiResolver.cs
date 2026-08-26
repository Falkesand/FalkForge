namespace FalkForge.Engine;

using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Resolves the extracted UI executable for the self-extract bootstrapper and proves it may be
/// launched. The direct counterpart of <see cref="BootstrapCompanionResolver"/>, and it exists for
/// the same reason: the UI is not an ordinary installable payload, it is a binary the engine
/// starts and then trusts.
///
/// <para><b>Why the UI needs this at all.</b> The bootstrapper hands the launched process the
/// session pipe name and the secret-pipe name, and whatever connects on that pipe drives the
/// install. On a companion-carrying bundle the engine behind that pipe holds an elevated gateway.
/// Before this resolver existed the bootstrapper picked its launch target by scanning the
/// extraction directory for any <c>.exe</c> payload whose id did not contain "Engine", with the
/// last match winning, and then fell back to an <c>ExePackage</c>'s build-machine source path.
/// Neither step checked anything.</para>
///
/// <para><b>The trust chain this completes.</b> Before this runs the bootstrapper has already
/// (1) bound the overlay TOC hashes to the ECDSA-signed manifest for signed bundles
/// (<c>BundleTrustGate</c> — the UI is inside the signed set like every payload), and (2) streamed
/// each payload to the cache while verifying its bytes against its TOC hash
/// (<see cref="BundleReader"/> — a tampered UI never lands on disk). This resolver adds the link
/// between them: the TOC hash the extractor trusted must equal the hash the manifest DECLARES for
/// the UI (<see cref="InstallerManifest.EngineUiSha256"/>), so bytes == TOC == declared (== signed,
/// when a signature is present).</para>
///
/// <para><b>Fail-closed rules.</b> A manifest that declares no UI resolves to
/// <see cref="BootstrapUiResolution.None"/> even when the TOC smuggles a payload under the
/// reserved id — an undeclared binary must never be launched and handed the pipe secret. A
/// declared UI that is absent from the TOC, whose hash disagrees, or whose extracted file is
/// missing is a <see cref="ErrorKind.SecurityError"/>.</para>
/// </summary>
internal static class BootstrapUiResolver
{
    /// <summary>
    /// Resolves and verifies the extracted UI under <paramref name="cacheDir"/>. Returns
    /// <see cref="BootstrapUiResolution.None"/> when the manifest declares no UI, the verified
    /// path plus the digest it was proven against on success, and a
    /// <see cref="ErrorKind.SecurityError"/> failure when a declared UI cannot be trusted.
    /// </summary>
    internal static Result<BootstrapUiResolution> Resolve(
        InstallerManifest manifest,
        IReadOnlyList<TocEntry> tocEntries,
        string cacheDir)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tocEntries);

        // No declaration -> no UI is ever launched. A TOC payload under the reserved id with no
        // manifest declaration stays an inert cache file.
        if (string.IsNullOrEmpty(manifest.EngineUiSha256))
            return Result<BootstrapUiResolution>.Success(BootstrapUiResolution.None);

        TocEntry? uiEntry = null;
        foreach (var entry in tocEntries)
        {
            if (string.Equals(entry.PackageId, UiPayload.PackageId, StringComparison.Ordinal))
            {
                uiEntry = entry;
                break;
            }
        }

        if (uiEntry is null)
            return Result<BootstrapUiResolution>.Failure(ErrorKind.SecurityError,
                $"The manifest declares a UI executable ({UiPayload.PackageId}) but the bundle " +
                "carries no such payload — corrupt or tampered bundle. Refusing to install; there " +
                "is nothing the engine can trust to drive the install.");

        // The value the extractor verified the UI's bytes against: the TOC hash for a full
        // payload, the reconstructed-file hash for a delta payload (the delta-blob hash is
        // irrelevant to trust — same rule as SignedPayloadTocVerifier).
        var boundHash = uiEntry.IsDelta ? uiEntry.ReconstructedSha256Hash : uiEntry.Sha256Hash;

        if (string.IsNullOrEmpty(boundHash)
            || !string.Equals(boundHash, manifest.EngineUiSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Result<BootstrapUiResolution>.Failure(ErrorKind.SecurityError,
                $"The UI payload ({UiPayload.PackageId}) does not match the manifest's declared " +
                $"hash ({manifest.EngineUiSha256}); the bundle carries {boundHash ?? "<none>"}. " +
                "The UI drives the install over the engine's session pipe, so a hash disagreement " +
                "is treated as tampering — refusing to install.");
        }

        // The bootstrapper's extraction loop wrote the UI to <cacheDir>\<PackageId> after verifying
        // its bytes against the (now manifest-bound) TOC hash. A missing file here means extraction
        // did not actually produce it — never continue as if it had.
        var extractedPath = Path.Combine(cacheDir, UiPayload.PackageId);
        if (!File.Exists(extractedPath))
            return Result<BootstrapUiResolution>.Failure(ErrorKind.SecurityError,
                $"The verified UI executable was not found at its extraction path " +
                $"({extractedPath}). Refusing to install.");

        // boundHash travels with the path. The caller does not launch the UI here — the launch
        // happens after external-container acquisition and after the whole pre-UI prerequisite
        // bootstrap. Whoever starts the process must be able to re-open the file and prove these
        // bytes again at that moment, because the extraction directory is user-writable for the
        // whole of that gap.
        return Result<BootstrapUiResolution>.Success(
            new BootstrapUiResolution(extractedPath, boundHash));
    }
}
