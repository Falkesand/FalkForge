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
        var keyResult = CreateKey(config);
        if (keyResult.IsFailure)
            return Result<string>.Failure(keyResult.Error);

        using var key = keyResult.Value;

        var files = new List<ManifestFileEntry>(entries.Count);
        foreach (var entry in entries)
            files.Add(new ManifestFileEntry { Name = entry.PackageId, Sha256 = entry.Sha256 });

        var envelope = IntegrityEnvelopeCodec.Sign(files, key);
        return IntegrityEnvelopeCodec.Serialize(envelope);
    }

    private static Result<ECDsa> CreateKey(IntegrityConfiguration? config)
    {
        var keyPath = config?.SigningKeyPath;
        if (string.IsNullOrEmpty(keyPath))
            return ECDsa.Create(ECCurve.NamedCurves.nistP256);

        if (!File.Exists(keyPath))
            return Result<ECDsa>.Failure(ErrorKind.SecurityError,
                $"SGN002: Signing key file not found at '{keyPath}'.");

        try
        {
            var pem = File.ReadAllText(keyPath);
            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            return ecdsa;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            return Result<ECDsa>.Failure(ErrorKind.SecurityError,
                $"SGN002: Failed to load signing key from '{keyPath}': {ex.Message}");
        }
    }
}
