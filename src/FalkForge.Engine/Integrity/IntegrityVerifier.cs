namespace FalkForge.Engine.Integrity;

using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Verifies the integrity of installer payloads laid out in a directory.
/// Validates the ECDSA-P256 signature over the file hash manifest, then verifies
/// each payload file's SHA-256 hash against the signed entries. The signature math
/// is shared with the build-time signer via <see cref="IntegrityEnvelopeCodec"/>.
/// </summary>
internal static class IntegrityVerifier
{
    /// <summary>
    /// Verifies manifest signature and payload file integrity for files in a directory.
    /// </summary>
    /// <param name="manifest">The installer manifest, potentially containing a signature envelope.</param>
    /// <param name="payloadDirectory">The directory containing payload files to verify.</param>
    /// <returns>
    /// Success if unsigned (backward compatible) or all checks pass.
    /// Failure with INT001 for invalid signature, INT002 for hash mismatch, INT003 for malformed envelope.
    /// </returns>
    internal static Result<Unit> Verify(InstallerManifest manifest, string payloadDirectory)
    {
        if (manifest.ManifestSignature is null)
            return Result<Unit>.Success(default);

        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
        if (envelope is null)
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT003: Failed to parse manifest signature envelope.");

        if (string.IsNullOrEmpty(envelope.PublicKey) || string.IsNullOrEmpty(envelope.Signature))
        {
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT003: Manifest signature envelope is missing required fields (publicKey or signature).");
        }

        if (!IntegrityEnvelopeCodec.VerifySignature(envelope))
        {
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT001: Manifest signature verification failed. The installer may have been tampered with.");
        }

        return VerifyFileHashes(envelope.Files, payloadDirectory);
    }

    private static Result<Unit> VerifyFileHashes(
        IReadOnlyList<ManifestFileEntry> files,
        string payloadDirectory)
    {
        var normalizedBase = Path.GetFullPath(payloadDirectory);

        foreach (var entry in files)
        {
            // Path traversal defense: resolve full path and ensure it stays within payload directory
            if (string.IsNullOrEmpty(entry.Name))
            {
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    "INT003: File entry has empty name in manifest signature.");
            }

            var candidatePath = Path.GetFullPath(Path.Combine(payloadDirectory, entry.Name));
            if (!candidatePath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT003: Path traversal detected in file entry '{entry.Name}'.");
            }

            if (!File.Exists(candidatePath))
            {
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT002: Payload file '{entry.Name}' not found. The installer may be incomplete or tampered with.");
            }

            var actualHash = ComputeSha256(candidatePath);
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    $"INT002: SHA-256 hash mismatch for '{entry.Name}'. Expected {entry.Sha256}, got {actualHash}.");
            }
        }

        return Result<Unit>.Success(default);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes);
    }
}
