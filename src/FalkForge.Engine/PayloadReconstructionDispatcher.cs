namespace FalkForge.Engine;

using FalkForge.Engine.Protocol.Bundle;

/// <summary>
/// Decides how a single TOC entry's payload gets from a bundle onto disk: a full payload streams
/// straight from the bundle (see <see cref="BundleReader.ExtractPayloadToFile"/>); a delta payload
/// is reconstructed against a base bundle (see <see cref="DeltaApplicator.ReconstructPayloadToFile"/>).
/// Extracted out of <see cref="Program"/> so the delta/no-base and delta/base-not-found fail-loud
/// branches — the whole point of a delta update, since they decide whether it can proceed at all —
/// are independently testable (GAP-1). Internal: reachable from <c>FalkForge.Engine.Tests</c> via
/// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/>.
/// </summary>
internal static class PayloadReconstructionDispatcher
{
    /// <summary>
    /// Extracts a payload to a file, reconstructing delta payloads against the base bundle.
    /// For a full payload this streams + verifies straight to disk (BundleReader). For a delta
    /// payload it reconstructs the finished payload from the base bundle via DeltaApplicator,
    /// verifying the reconstructed SHA-256 before anything is published. A delta payload with no
    /// base bundle available fails loudly — the raw delta blob is never written as if it were the
    /// finished payload, and the honest recovery is to download the full (non-delta) installer.
    /// </summary>
    internal static Result<string> Dispatch(
        string bundlePath, TocEntry entry, string destinationDirectory, string relativeDestination, string? baseBundlePath)
    {
        if (!entry.IsDelta)
            return BundleReader.ExtractPayloadToFile(bundlePath, entry, destinationDirectory, relativeDestination);

        if (string.IsNullOrEmpty(baseBundlePath))
            return Result<string>.Failure(ErrorKind.BundleError,
                $"Payload '{entry.PackageId}' is a delta payload but no base bundle is available to reconstruct it. " +
                "A delta update requires the exact previously-installed bundle as its base; pass " +
                "--base-bundle <path> or download the full installer instead.");

        if (!File.Exists(baseBundlePath))
            return Result<string>.Failure(ErrorKind.BundleError,
                $"Payload '{entry.PackageId}' is a delta payload but the base bundle " +
                $"'{Path.GetFileName(baseBundlePath)}' was not found. Download the full installer instead.");

        return DeltaApplicator.ReconstructPayloadToFile(
            bundlePath, entry, baseBundlePath, destinationDirectory, relativeDestination);
    }
}
