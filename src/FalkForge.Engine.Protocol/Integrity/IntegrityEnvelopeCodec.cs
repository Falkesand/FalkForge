namespace FalkForge.Engine.Protocol.Integrity;

using System.Collections.Frozen;
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
/// fields out of their own signed payload, and — crucially — makes the signed bytes
/// <b>byte-identical across v1 and v2</b>, so a v2 multi-signature envelope and a legacy
/// v1 envelope compute the same message. That is what makes backward compatibility clean.</para>
///
/// <para><b>Trust.</b> The embedded public keys are self-describing: an attacker can re-sign a
/// rewritten bundle with their own key. Authorship is established only by
/// <see cref="VerifyTrusted"/>, which accepts a signature solely when its key's fingerprint is in
/// a trusted set pinned <i>outside</i> the bundle (the engine's baked set). <see cref="VerifySignature"/>
/// remains a consistency-only check (any embedded signature verifies against its own embedded key)
/// used where trust is not being established.</para>
/// </summary>
public static class IntegrityEnvelopeCodec
{
    /// <summary>The algorithm identifier embedded in produced envelopes.</summary>
    public const string AlgorithmId = "ECDSA-P256";

    /// <summary>The current envelope format version (v2 = signature list).</summary>
    public const int CurrentVersion = 2;

    /// <summary>The keyId assigned to a v1 envelope's signature when it is adapted to the list shape.</summary>
    public const string LegacyKeyId = "legacy";

    /// <summary>
    /// Computes the canonical files-only signed bytes (epoch 0, no revocations). Byte-identical across
    /// v1 and v2 envelopes. Retained so existing callers and already-signed bundles keep the exact
    /// legacy message; delegates to the epoch/revocation-aware overload with the neutral values.
    /// </summary>
    public static byte[] ComputeSignedBytes(IReadOnlyList<ManifestFileEntry> files) =>
        ComputeSignedBytes(files, epoch: 0, revoked: []);

    /// <summary>
    /// Computes the canonical bytes that are hashed and signed. The base message is the UTF-8 encoding
    /// of the source-generated JSON for the file entries array (unchanged from v1).
    ///
    /// <para><b>Backward-compatibility rule (§6.3, the epoch compat trap).</b> The key epoch and the
    /// revocation list are folded into the signed message <b>only when present</b> — a non-zero
    /// <paramref name="epoch"/> or a non-empty <paramref name="revoked"/> list. When both are neutral
    /// (epoch 0, no revocations) the message is exactly <c>UTF-8(JSON(files))</c>, byte-identical to what
    /// v1 and Stage-1 v2 envelopes signed, so every already-shipped bundle still verifies. When either is
    /// present it is appended under a unit-separator (U+001F, which never appears in the JSON), so an
    /// attacker cannot lower the epoch or strip/forge a revocation without invalidating the signature. A
    /// v1 bundle is always treated as epoch 0 with no revocations.</para>
    /// </summary>
    public static byte[] ComputeSignedBytes(
        IReadOnlyList<ManifestFileEntry> files, int epoch, IReadOnlyList<string> revoked)
    {
        var filesJson = JsonSerializer.Serialize(
            files, IntegrityEnvelopeJsonContext.Default.IReadOnlyListManifestFileEntry);

        // Neutral (epoch 0, no revocations) → the exact legacy files-only bytes. This is the property
        // that keeps v1 and Stage-1 v2 (epoch-0) envelopes verifiable after Stage 2.
        if (epoch == 0 && (revoked is null || revoked.Count == 0))
            return Encoding.UTF8.GetBytes(filesJson);

        // Present → bind epoch + revocations into the signed message under a separator that cannot occur
        // in the JSON, so they are cryptographically covered. The revocation list is serialized with the
        // same source-generated JSON as `files` (NOT a plain comma-join): a comma-join is non-injective —
        // ["FP1","FP2"] and ["FP1,FP2"] would produce identical signed bytes, letting an attacker
        // restructure a legit-signed revocation list without breaking the signature. JSON quotes each
        // element, so distinct lists always produce distinct signed bytes.
        var revokedJson = JsonSerializer.Serialize(
            revoked ?? (IReadOnlyList<string>)[], IntegrityEnvelopeJsonContext.Default.IReadOnlyListString);

        var sb = new StringBuilder(filesJson);
        sb.Append('').Append("epoch=").Append(epoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('').Append("revoked=").Append(revokedJson);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// The SHA-256 fingerprint (uppercase hex, no separators) of a SubjectPublicKeyInfo blob —
    /// the value matched against a trusted set.
    /// </summary>
    public static string ComputeFingerprint(byte[] subjectPublicKeyInfo)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        return Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo));
    }

    /// <summary>
    /// Builds and signs a v2 envelope for the supplied file entries using a single
    /// <paramref name="key"/>. The caller owns the key's lifetime. Convenience overload for the
    /// common single-key case — delegates to the multi-key <see cref="Sign(IReadOnlyList{ManifestFileEntry}, IReadOnlyList{ECDsa})"/>.
    /// </summary>
    public static ManifestSignatureEnvelope Sign(IReadOnlyList<ManifestFileEntry> files, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Sign(files, [key]);
    }

    /// <summary>
    /// Builds and signs a v2 envelope for the supplied file entries using one or more keys
    /// (rotation-safe dual-sign), with no epoch and no revocations. The caller owns each key's lifetime.
    /// </summary>
    public static ManifestSignatureEnvelope Sign(IReadOnlyList<ManifestFileEntry> files, IReadOnlyList<ECDsa> keys) =>
        Sign(files, keys, epoch: 0, revoked: []);

    /// <summary>
    /// Builds and signs a v2 envelope for the supplied file entries using one or more keys
    /// (rotation-safe dual-sign). Every key signs the identical message — <c>SHA-256</c> of
    /// <see cref="ComputeSignedBytes(IReadOnlyList{ManifestFileEntry}, int, IReadOnlyList{string})"/> —
    /// and contributes one <see cref="SignatureEntry"/>. A non-zero <paramref name="epoch"/> or a
    /// non-empty <paramref name="revoked"/> list is cryptographically covered by the signature (§6.3).
    /// The caller owns each key's lifetime.
    /// </summary>
    public static ManifestSignatureEnvelope Sign(
        IReadOnlyList<ManifestFileEntry> files, IReadOnlyList<ECDsa> keys, int epoch, IReadOnlyList<string> revoked)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(revoked);
        if (keys.Count == 0)
            throw new ArgumentException("At least one signing key is required.", nameof(keys));

        var hash = SHA256.HashData(ComputeSignedBytes(files, epoch, revoked));

        var signatures = new List<SignatureEntry>(keys.Count);
        foreach (var key in keys)
        {
            ArgumentNullException.ThrowIfNull(key);
            var spki = key.ExportSubjectPublicKeyInfo();
            signatures.Add(new SignatureEntry
            {
                KeyId = string.Empty,
                Fingerprint = ComputeFingerprint(spki),
                PublicKey = Convert.ToBase64String(spki),
                Signature = Convert.ToBase64String(key.SignHash(hash))
            });
        }

        return new ManifestSignatureEnvelope
        {
            Version = CurrentVersion,
            Algorithm = AlgorithmId,
            Files = files,
            Signatures = signatures,
            Epoch = epoch,
            Revoked = revoked
        };
    }

    /// <summary>Serializes an envelope to its canonical JSON wire form.</summary>
    public static string Serialize(ManifestSignatureEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);

    /// <summary>
    /// Parses envelope JSON, normalizing both wire shapes into the v2 list form. A v1 envelope
    /// (top-level <c>publicKey</c> + <c>signature</c>, no <c>signatures</c>) is adapted into a
    /// one-element <see cref="ManifestSignatureEnvelope.Signatures"/> list so every downstream
    /// consumer only sees the list. Returns <c>null</c> when the JSON is malformed so callers can
    /// map that to a typed integrity error rather than letting an exception escape.
    /// </summary>
    public static ManifestSignatureEnvelope? Parse(string json)
    {
        ManifestSignatureEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(
                json, IntegrityEnvelopeJsonContext.Default.ManifestSignatureEnvelope);
        }
        catch (JsonException)
        {
            return null;
        }

        if (envelope is null)
            return null;

        // v1 → v2 adapter: a legacy envelope carries a single top-level publicKey + signature and no
        // signatures list. Synthesize the equivalent one-element list so the verifier is shape-agnostic.
        if (envelope.Signatures.Count == 0
            && !string.IsNullOrEmpty(envelope.PublicKey)
            && !string.IsNullOrEmpty(envelope.Signature))
        {
            var fingerprint = string.Empty;
            try
            {
                fingerprint = ComputeFingerprint(Convert.FromBase64String(envelope.PublicKey));
            }
            catch (FormatException)
            {
                // A garbage (non-base64) public key yields an empty fingerprint; VerifyTrusted then
                // skips the entry when it fails to import the key, so a corrupt v1 envelope cannot verify.
            }

            envelope.Signatures =
            [
                new SignatureEntry
                {
                    KeyId = LegacyKeyId,
                    Fingerprint = fingerprint,
                    PublicKey = envelope.PublicKey,
                    Signature = envelope.Signature
                }
            ];
        }

        return envelope;
    }

    /// <summary>
    /// Consistency-only check: returns true when <b>any</b> embedded signature verifies against its
    /// own embedded public key. This proves internal tamper-evidence, <b>not</b> authorship (the key
    /// is self-describing). Use <see cref="VerifyTrusted"/> to establish authorship against a pinned
    /// trusted set. Returns false on any cryptographic or encoding failure.
    /// </summary>
    public static bool VerifySignature(ManifestSignatureEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        // An empty trusted set means "accept any fingerprint that self-verifies" (§5.1) — exactly the
        // consistency-only semantics this method has always had.
        return VerifyTrusted(envelope, FrozenSet<string>.Empty).IsSuccess;
    }

    /// <summary>
    /// Verifies the envelope against a pinned <paramref name="trustedFingerprints"/> set using the
    /// verify-any rule: the envelope is accepted as soon as <b>one</b> signature both
    /// (a) re-derives to its declared fingerprint, (b) has that fingerprint in the trusted set, and
    /// (c) cryptographically verifies. A lying fingerprint (one that does not match its own key) or an
    /// untrusted key is skipped, never trusted. The first valid trusted signature wins, so a
    /// dual-signed bundle is accepted by any engine that trusts either key.
    ///
    /// <para>When <paramref name="trustedFingerprints"/> is empty the trust check in (b) is bypassed —
    /// every self-verifying signature is acceptable. This preserves consistency-only behavior for
    /// callers with no baked set (an unconfigured engine); establishing authorship requires a
    /// non-empty pinned set.</para>
    /// </summary>
    /// <returns>
    /// Success when a trusted, valid signature is found. INT003 when the envelope carries no usable
    /// signatures. INT001 when no signature both matches a trusted fingerprint and verifies.
    /// </returns>
    public static Result<Unit> VerifyTrusted(
        ManifestSignatureEnvelope envelope,
        IReadOnlySet<string> trustedFingerprints)
    {
        var match = MatchTrustedSignature(envelope, trustedFingerprints);
        return match.IsSuccess ? Result<Unit>.Success(default) : Result<Unit>.Failure(match.Error);
    }

    /// <summary>
    /// Same verify-any rule as <see cref="VerifyTrusted"/>, but returns the <b>matched fingerprint</b> of
    /// the accepted signature on success. Callers that enforce revocation (§6.3 step 3) need to know which
    /// key was trusted so they can reject it when it appears in the persisted revocation list.
    /// </summary>
    /// <returns>
    /// Success carrying the accepted signature's fingerprint (uppercase hex). INT003 when the envelope
    /// carries no usable signatures. INT001 when no signature both matches a trusted fingerprint and verifies.
    /// </returns>
    public static Result<string> MatchTrustedSignature(
        ManifestSignatureEnvelope envelope,
        IReadOnlySet<string> trustedFingerprints)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(trustedFingerprints);

        var signatures = envelope.Signatures;
        if (signatures.Count == 0)
            return Result<string>.Failure(ErrorKind.IntegrityError,
                "INT003: Manifest integrity envelope carries no signatures.");

        // The signed message covers the files and, when present, the epoch + revocation list (§6.3), so
        // compute the hash once for all entries. Epoch 0 + no revocations reproduces the legacy files-only
        // bytes, keeping v1 and Stage-1 v2 envelopes verifiable.
        var hash = SHA256.HashData(ComputeSignedBytes(envelope.Files, envelope.Epoch, envelope.Revoked));
        var haveTrustSet = trustedFingerprints.Count > 0;

        foreach (var entry in signatures)
        {
            if (string.IsNullOrEmpty(entry.PublicKey) || string.IsNullOrEmpty(entry.Signature))
                continue;

            byte[] spki;
            byte[] signatureBytes;
            try
            {
                spki = Convert.FromBase64String(entry.PublicKey);
                signatureBytes = Convert.FromBase64String(entry.Signature);
            }
            catch (FormatException)
            {
                continue; // malformed entry — try the next signature
            }

            // (a) The declared fingerprint MUST equal the fingerprint of its own key. A lying
            // fingerprint (crafted to sit in the trusted set while carrying a different key) is
            // rejected — never trust a fingerprint that does not match the key it travels with.
            var actualFingerprint = ComputeFingerprint(spki);
            if (!string.Equals(actualFingerprint, entry.Fingerprint, StringComparison.OrdinalIgnoreCase))
                continue;

            // (b) The key must be pinned. With no baked set, this check is bypassed (consistency-only).
            if (haveTrustSet && !trustedFingerprints.Contains(actualFingerprint))
                continue;

            // (c) The signature must cryptographically verify over the signed message.
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
                if (ecdsa.VerifyHash(hash, signatureBytes))
                    return Result<string>.Success(actualFingerprint);
            }
            catch (CryptographicException)
            {
                // Import/verify failed for this entry — fall through to try the next signature.
            }
        }

        return Result<string>.Failure(ErrorKind.IntegrityError,
            "INT001: No trusted signature validates the manifest. The bundle may have been tampered " +
            "with or signed by an untrusted publisher.");
    }
}
