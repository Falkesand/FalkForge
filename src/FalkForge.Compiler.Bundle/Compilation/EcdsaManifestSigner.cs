using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Models;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Build-time payload signer using pure .NET ECDSA (P-256). Produces a
/// <see cref="ManifestSignatureEnvelope"/> the engine verifies before executing any
/// payload, independent of Authenticode and of the external <c>sigil</c> CLI.
///
/// <para>Key handling follows the integrity design: when
/// <see cref="IntegrityConfiguration.SigningKeyPath"/> is set, the PEM private key at
/// that path is used (stable public key = authorship proof across builds); otherwise a
/// throwaway ephemeral key is generated per build for zero-config tamper detection.
/// Cert-store and vault key sources remain the sigil CLI's responsibility and are not
/// handled here.</para>
///
/// <para>The signature is a post-content addition: it is computed over the payload
/// hashes and embedded in the manifest envelope, never folded into any reproducible
/// content digest. ECDSA is non-deterministic, so a build using <c>Reproducible()</c>
/// stays byte-reproducible in its payload content while the envelope (key + signature)
/// is the one intentionally non-deterministic, post-content part.</para>
/// </summary>
internal static class EcdsaManifestSigner
{
    /// <summary>
    /// Signs the supplied payload hashes and returns the canonical envelope JSON to embed
    /// in the manifest. Returns SGN002 when a configured key file is missing or unreadable.
    /// </summary>
    internal static Result<string> Sign(
        IReadOnlyList<PayloadHashEntry> entries,
        IntegrityConfiguration? config)
    {
        var keysResult = CreateKeys(config);
        if (keysResult.IsFailure)
            return Result<string>.Failure(keysResult.Error);

        var keys = keysResult.Value;
        try
        {
            var files = new List<ManifestFileEntry>(entries.Count);
            foreach (var entry in entries)
                files.Add(new ManifestFileEntry { Name = entry.PackageId, Sha256 = entry.Sha256 });

            // Fold the publisher's epoch + declared revocations into the signed message (C14 Stage 2).
            // Neutral values (epoch 0, no revocations) reproduce the legacy files-only signed bytes, so
            // an unchanged config keeps producing byte-identical signatures across v1/v2.
            var epoch = config?.Epoch ?? 0;
            IReadOnlyList<string> revoked = config?.RevokedFingerprints ?? [];

            var envelope = IntegrityEnvelopeCodec.Sign(files, keys, epoch, revoked);
            return IntegrityEnvelopeCodec.Serialize(envelope);
        }
        finally
        {
            foreach (var key in keys)
                key.Dispose();
        }
    }

    /// <summary>
    /// Resolves the signing keys. Prefers <see cref="IntegrityConfiguration.SigningKeyPaths"/> (one or
    /// more PEM keys, rotation-safe dual-sign); falls back to the single
    /// <see cref="IntegrityConfiguration.SigningKeyPath"/>; and finally to a throwaway ephemeral key for
    /// zero-config tamper detection. Ownership of every returned <see cref="ECDsa"/> transfers to the
    /// caller (<see cref="Sign"/>), which disposes them in a finally block. Returns SGN002 when a
    /// configured key file is missing or unreadable — any keys already created are disposed first.
    /// </summary>
    private static Result<IReadOnlyList<ECDsa>> CreateKeys(IntegrityConfiguration? config)
    {
        var paths = ResolveKeyPaths(config);

        if (paths.Count == 0)
        {
#pragma warning disable IDISP005 // Ownership transferred to caller (Sign) which disposes in a finally block.
            return Result<IReadOnlyList<ECDsa>>.Success([ECDsa.Create(ECCurve.NamedCurves.nistP256)]);
#pragma warning restore IDISP005
        }

        var keys = new List<ECDsa>(paths.Count);
        foreach (var keyPath in paths)
        {
            var keyResult = LoadPemKey(keyPath);
            if (keyResult.IsFailure)
            {
                foreach (var created in keys)
                    created.Dispose();
                return Result<IReadOnlyList<ECDsa>>.Failure(keyResult.Error);
            }

            keys.Add(keyResult.Value);
        }

        // Result<T>'s implicit conversion cannot originate from an interface type, so use the factory.
        return Result<IReadOnlyList<ECDsa>>.Success(keys);
    }

    private static IReadOnlyList<string> ResolveKeyPaths(IntegrityConfiguration? config)
    {
        if (config?.SigningKeyPaths is { Count: > 0 } multi)
            return multi;
        if (!string.IsNullOrEmpty(config?.SigningKeyPath))
            return [config.SigningKeyPath];
        return [];
    }

    private static Result<ECDsa> LoadPemKey(string keyPath)
    {
        if (!File.Exists(keyPath))
            return Result<ECDsa>.Failure(ErrorKind.SecurityError,
                $"SGN002: Signing key file not found at '{keyPath}'.");

        try
        {
            var pem = File.ReadAllText(keyPath);
            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
#pragma warning disable IDISP005 // Ownership transferred to caller (Sign) which disposes in a finally block.
            return ecdsa;
#pragma warning restore IDISP005
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            return Result<ECDsa>.Failure(ErrorKind.SecurityError,
                $"SGN002: Failed to load signing key from '{keyPath}': {ex.Message}");
        }
    }
}
