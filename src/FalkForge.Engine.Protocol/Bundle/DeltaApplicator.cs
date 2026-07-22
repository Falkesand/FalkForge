using System.Buffers;
using System.Security.Cryptography;
using Octodiff.Core;
using Octodiff.Diagnostics;

namespace FalkForge.Engine.Protocol.Bundle;

/// <summary>
/// Install-time counterpart to the build-side <c>DeltaBundleCompiler</c>: reconstructs a delta
/// payload back into its finished form by applying the embedded Octodiff delta blob to the matching
/// payload from the base (old) bundle.
///
/// <para>
/// A delta <see cref="TocEntry"/> stores three hashes with distinct meanings:
/// <list type="bullet">
///   <item><description><see cref="TocEntry.Sha256Hash"/> — hash of the <b>delta blob</b> itself
///   (what <see cref="BundleReader"/> already verifies after gzip-decompression).</description></item>
///   <item><description><see cref="TocEntry.BaseSha256Hash"/> — hash of the <b>base payload</b> the
///   delta was computed against; used here to prove the supplied basis is the correct old
///   version.</description></item>
///   <item><description><see cref="TocEntry.ReconstructedSha256Hash"/> — hash of the <b>finished
///   payload</b> after the delta is applied; the final integrity gate.</description></item>
/// </list>
/// </para>
///
/// <para><b>Fail-loud contract.</b> Every failure mode — base bundle unreadable, base payload
/// absent, base payload hash mismatch (wrong old version), delta-blob verification failure,
/// Octodiff apply error, or a reconstructed payload whose hash does not match
/// <see cref="TocEntry.ReconstructedSha256Hash"/> — returns a failure <see cref="Result{T}"/> and
/// writes no output file. Unverified (possibly tampered) bytes are never published to the
/// destination. Callers that cannot obtain the base bundle at install time must treat that as a
/// hard failure and recover by downloading the full (non-delta) installer — a delta bundle cannot
/// be applied without its exact base.</para>
///
/// <para><b>Memory.</b> The base payload, the delta blob, and the reconstructed payload each stream
/// through temporary files (never a whole-payload <c>byte[]</c>); only a small pooled copy buffer is
/// held resident. Octodiff's copy operations require a seekable basis, so the base payload is
/// materialised to a temp file rather than a forward-only stream.</para>
/// </summary>
public static class DeltaApplicator
{
    private const int CopyBufferSize = 64 * 1024;

    /// <summary>
    /// Reconstructs the delta payload described by <paramref name="deltaEntry"/> (read from
    /// <paramref name="deltaBundlePath"/>) using the matching base payload from
    /// <paramref name="basisBundlePath"/>, writing the verified finished payload to
    /// <paramref name="relativeDestination"/> resolved inside <paramref name="destinationDirectory"/>.
    /// The destination is derived from the (untrusted) TOC <see cref="TocEntry.PackageId"/>, so the
    /// same path-containment choke point as <see cref="BundleReader.ExtractPayloadToFile(string, TocEntry, string, string)"/>
    /// is applied. Returns the resolved destination path on success.
    /// </summary>
    public static Result<string> ReconstructPayloadToFile(
        string deltaBundlePath,
        TocEntry deltaEntry,
        string basisBundlePath,
        string destinationDirectory,
        string relativeDestination)
    {
        ArgumentNullException.ThrowIfNull(deltaEntry);

        if (!ContainedPathResolver.TryResolveContained(destinationDirectory, relativeDestination, out var destinationPath))
        {
            return Result<string>.Failure(ErrorKind.SecurityError,
                $"Delta payload '{deltaEntry.PackageId}' resolves outside the destination directory " +
                $"'{destinationDirectory}' — rejecting crafted bundle (path traversal / zip-slip).");
        }

        var parentDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(parentDir))
            Directory.CreateDirectory(parentDir);

        var reconstructResult = ReconstructPayloadToFile(deltaBundlePath, deltaEntry, basisBundlePath, destinationPath);
        return reconstructResult.IsFailure
            ? Result<string>.Failure(reconstructResult.Error)
            : destinationPath;
    }

    /// <summary>
    /// Trusted-path overload: writes the reconstructed payload verbatim to
    /// <paramref name="destinationPath"/> with no containment check. Only ever call with a
    /// caller-fixed, trusted path (destinations derived from an untrusted TOC name must use the
    /// public overload above).
    /// </summary>
    internal static Result<Unit> ReconstructPayloadToFile(
        string deltaBundlePath,
        TocEntry deltaEntry,
        string basisBundlePath,
        string destinationPath)
    {
        if (!deltaEntry.IsDelta)
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"Payload '{deltaEntry.PackageId}' is not a delta payload — DeltaApplicator must not be used for it.");

        if (string.IsNullOrEmpty(deltaEntry.BaseSha256Hash) || string.IsNullOrEmpty(deltaEntry.ReconstructedSha256Hash))
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"Delta payload '{deltaEntry.PackageId}' is missing its base/reconstructed hash metadata — malformed delta bundle.");

        // Locate the matching base payload in the base bundle's TOC.
        var basisContentResult = BundleReader.Extract(basisBundlePath);
        if (basisContentResult.IsFailure)
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"Cannot read base bundle '{Path.GetFileName(basisBundlePath)}' required to apply delta for " +
                $"'{deltaEntry.PackageId}': {basisContentResult.Error.Message}");

        var basisEntry = Array.Find(basisContentResult.Value.TocEntries,
            e => string.Equals(e.PackageId, deltaEntry.PackageId, StringComparison.OrdinalIgnoreCase));

        if (basisEntry is null)
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"Base bundle does not contain payload '{deltaEntry.PackageId}' — cannot apply delta " +
                "(wrong or missing base version). Recover by downloading the full installer.");

        // The base payload the delta was computed against must be exactly the payload we have.
        // A mismatch means the supplied basis is the wrong old version; applying the delta would
        // silently produce corrupt output, so refuse before doing any work.
        if (!string.Equals(basisEntry.Sha256Hash, deltaEntry.BaseSha256Hash, StringComparison.OrdinalIgnoreCase))
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                $"Base payload '{deltaEntry.PackageId}' hash does not match the delta's expected base " +
                "— wrong base version. Recover by downloading the full installer.");

        var basisTempPath = $"{destinationPath}.{Guid.NewGuid():N}.basis.tmp";
        var deltaTempPath = $"{destinationPath}.{Guid.NewGuid():N}.delta.tmp";
        var reconstructedTempPath = $"{destinationPath}.{Guid.NewGuid():N}.recon.tmp";

        try
        {
            // Extract + verify the base payload (verifies basisEntry.Sha256Hash) to a seekable temp
            // file — Octodiff's copy operations reference arbitrary offsets into the basis.
            var basisExtract = BundleReader.ExtractPayloadToFile(basisBundlePath, basisEntry, basisTempPath);
            if (basisExtract.IsFailure)
                return Result<Unit>.Failure(basisExtract.Error);

            // Extract + verify the delta blob (verifies deltaEntry.Sha256Hash — the delta-blob hash)
            // to a temp file. This is the same gzip-decompress-and-verify path used for full
            // payloads; for a delta entry the decompressed bytes ARE the raw delta blob.
            var deltaExtract = BundleReader.ExtractPayloadToFile(deltaBundlePath, deltaEntry, deltaTempPath);
            if (deltaExtract.IsFailure)
                return Result<Unit>.Failure(deltaExtract.Error);

            // Apply the delta to the base, streaming to a temp file.
            var applyResult = ApplyDelta(basisTempPath, deltaTempPath, reconstructedTempPath);
            if (applyResult.IsFailure)
                return Result<Unit>.Failure(applyResult.Error);

            // Final integrity gate: the reconstructed payload must hash to the compiler-recorded
            // ReconstructedSha256Hash. Only after this passes is the payload published to the
            // destination — an unverified reconstruction is never reachable at destinationPath.
            var actualHash = HashFile(reconstructedTempPath);
            if (!string.Equals(actualHash, deltaEntry.ReconstructedSha256Hash, StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"Reconstructed payload '{deltaEntry.PackageId}' failed integrity check — SHA-256 does not " +
                    "match the expected reconstructed hash. Refusing to write unverified output.");

            File.Move(reconstructedTempPath, destinationPath, overwrite: true);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"Failed to reconstruct delta payload '{deltaEntry.PackageId}': {ex.Message}");
        }
        finally
        {
            TryDeleteFile(basisTempPath);
            TryDeleteFile(deltaTempPath);
            TryDeleteFile(reconstructedTempPath);
        }
    }

    private static Result<Unit> ApplyDelta(string basisPath, string deltaPath, string outputPath)
    {
        try
        {
            using var basisStream = new FileStream(basisPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var deltaStream = new FileStream(deltaPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            // ReadWrite (not write-only): Octodiff's DeltaApplier with SkipHashCheck=false reads the
            // reconstructed output back to verify the delta's declared target hash, so the output
            // stream must be readable + seekable.
            using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            var applier = new DeltaApplier { SkipHashCheck = false };
            applier.Apply(
                basisStream,
                new BinaryDeltaReader(deltaStream, NullProgressReporter.Instance),
                outputStream);

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException
                                       or CorruptFileFormatException or UsageException)
        {
            // CorruptFileFormatException/UsageException are Octodiff's own apply-time failure
            // types (BinaryDeltaReader.EnsureMetadata / DeltaApplier.Apply) — both derive directly
            // from System.Exception with no shared base with the IOException family above, so they
            // must be named explicitly. Malformed-but-hash-matching delta bytes (e.g. corrupted in
            // a way that preserves the blob hash, or produced by a mismatched pipeline) hit this
            // path; the fail-loud contract documented on this type requires a Result, not a crash.
            return Result<Unit>.Failure(ErrorKind.BundleError, $"Delta application failed: {ex.Message}");
        }
    }

    private static string HashFile(string path)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hasher.AppendData(buffer, 0, read);

            return Convert.ToHexString(hasher.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup; a failure Result (if any) already told the caller the payload is bad.
        }
    }
}
