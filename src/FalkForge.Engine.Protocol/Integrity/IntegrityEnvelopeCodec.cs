namespace FalkForge.Engine.Protocol.Integrity;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Canonical encode/sign/verify helpers for the integrity envelope. Centralizing
/// the byte computation here guarantees the build-time signer and the runtime
/// verifier agree to the byte — there is no second implementation that could drift.
///
/// <para>Algorithm: ECDSA over the NIST P-256 curve. The signed message is
/// <c>SHA-256(UTF-8(JSON(files)))</c> where <c>JSON(files)</c> is the source-generated
/// serialization of the <see cref="ManifestSignatureEnvelope.Files"/> array. Signing
/// only the file list (not the whole envelope) keeps the public key and signature
/// fields out of their own signed payload.</para>
/// </summary>
public static class IntegrityEnvelopeCodec
{
    /// <summary>The algorithm identifier embedded in produced envelopes.</summary>
    public const string AlgorithmId = "ECDSA-P256";

    /// <summary>The current envelope format version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Computes the canonical bytes that are hashed and signed: the UTF-8 encoding of
    /// the source-generated JSON for the file entries array.
    /// </summary>
    public static byte[] ComputeSignedBytes(IReadOnlyList<ManifestFileEntry> files)
    {
        var filesJson = JsonSerializer.Serialize(
            files, IntegrityEnvelopeJsonContext.Default.IReadOnlyListManifestFileEntry);
        return Encoding.UTF8.GetBytes(filesJson);
    }

    /// <summary>
    /// Builds and signs an envelope for the supplied file entries using <paramref name="key"/>.
    /// The caller owns the key's lifetime. The envelope embeds the SubjectPublicKeyInfo
    /// public half so the verifier needs nothing else.
    /// </summary>
    public static ManifestSignatureEnvelope Sign(IReadOnlyList<ManifestFileEntry> files, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var hash = SHA256.HashData(ComputeSignedBytes(files));
        var signature = key.SignHash(hash);

        return new ManifestSignatureEnvelope
        {
            Version = CurrentVersion,
            Algorithm = AlgorithmId,
            PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            Files = files,
            Signature = Convert.ToBase64String(signature)
        };
    }

    /// <summary>Serializes an envelope to its canonical JSON wire form.</summary>
    public static string Serialize(ManifestSignatureEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);

    /// <summary>
    /// Parses envelope JSON. Returns <c>null</c> when the JSON is malformed so callers
    /// can map that to a typed integrity error rather than letting an exception escape.
    /// </summary>
    public static ManifestSignatureEnvelope? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(
                json, IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies the envelope's ECDSA signature over its file entries using the embedded
    /// public key. Returns false on any cryptographic or encoding failure. This checks
    /// only the signature — callers separately verify each file's bytes against the entries.
    /// </summary>
    public static bool VerifySignature(ManifestSignatureEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (string.IsNullOrEmpty(envelope.PublicKey) || string.IsNullOrEmpty(envelope.Signature))
            return false;

        byte[] publicKeyBytes;
        byte[] signatureBytes;
        try
        {
            publicKeyBytes = Convert.FromBase64String(envelope.PublicKey);
            signatureBytes = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException)
        {
            return false;
        }

        var hash = SHA256.HashData(ComputeSignedBytes(envelope.Files));

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
            return ecdsa.VerifyHash(hash, signatureBytes);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
