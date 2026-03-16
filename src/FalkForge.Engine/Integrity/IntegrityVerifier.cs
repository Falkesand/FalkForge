namespace FalkForge.Engine.Integrity;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Verifies the integrity of installer payloads at engine runtime.
/// Validates the ECDSA-P256 signature over the file hash manifest,
/// then verifies each payload file's SHA-256 hash against the signed entries.
/// </summary>
internal static class IntegrityVerifier
{
    /// <summary>
    /// Verifies manifest signature and payload file integrity.
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

        // Parse the signature envelope
        ManifestSignatureEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(
                manifest.ManifestSignature,
                IntegritySignatureContext.Default.ManifestSignatureEnvelope);
        }
        catch (JsonException)
        {
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT003: Failed to parse manifest signature envelope.");
        }

        if (envelope is null)
        {
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT003: Manifest signature envelope is null after deserialization.");
        }

        // Validate required fields
        if (string.IsNullOrEmpty(envelope.PublicKey) || string.IsNullOrEmpty(envelope.Signature))
        {
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT003: Manifest signature envelope is missing required fields (publicKey or signature).");
        }

        // Verify ECDSA signature over the files list
        var signatureResult = VerifySignature(envelope);
        if (signatureResult.IsFailure)
            return signatureResult;

        // Verify each file's SHA-256 hash
        return VerifyFileHashes(envelope.Files, payloadDirectory);
    }

    private static Result<Unit> VerifySignature(ManifestSignatureEnvelope envelope)
    {
        byte[] publicKeyBytes;
        byte[] signatureBytes;

        try
        {
            publicKeyBytes = Convert.FromBase64String(envelope.PublicKey);
            signatureBytes = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException)
        {
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT003: Invalid base64 encoding in manifest signature envelope.");
        }

        // Recompute the hash over the serialized files array
        var filesJson = JsonSerializer.Serialize(
            envelope.Files,
            IntegritySignatureContext.Default.IReadOnlyListManifestFileEntry);
        var filesBytes = Encoding.UTF8.GetBytes(filesJson);
        var hash = SHA256.HashData(filesBytes);

        // Verify using ECDSA-P256
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);

            if (!ecdsa.VerifyHash(hash, signatureBytes))
            {
                return Result<Unit>.Failure(ErrorKind.IntegrityError,
                    "INT001: Manifest signature verification failed. The installer may have been tampered with.");
            }
        }
        catch (CryptographicException)
        {
            return Result<Unit>.Failure(ErrorKind.IntegrityError,
                "INT001: Manifest signature verification failed due to invalid cryptographic data.");
        }

        return Result<Unit>.Success(default);
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
