namespace FalkForge.Engine.Protocol.Integrity;

using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Signing;

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

    /// <summary>
    /// The per-entry algorithm identifier of an ML-DSA-65 (FIPS 204) post-quantum signature
    /// (PQ-hybrid Stage 1). Wire value, frozen. An entry with no algorithm field is
    /// <see cref="AlgorithmId"/> (ECDSA-P256).
    /// </summary>
    public const string MlDsa65AlgorithmId = FalkForge.Signing.SignatureAlgorithms.MlDsa65;

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
        ComputeSignedBytes(files, epoch: 0, revoked: [], externalContainers: null);

    /// <summary>
    /// Epoch/revocation-aware overload retained for existing callers. Delegates to the container-aware
    /// overload with no external containers, so the signed bytes are byte-identical to before the A6
    /// external-container hardening.
    /// </summary>
    public static byte[] ComputeSignedBytes(
        IReadOnlyList<ManifestFileEntry> files, int epoch, IReadOnlyList<string> revoked) =>
        ComputeSignedBytes(files, epoch, revoked, externalContainers: null);

    /// <summary>
    /// Computes the canonical bytes that are hashed and signed. The base message is the UTF-8 encoding
    /// of the source-generated JSON for the file entries array (unchanged from v1).
    ///
    /// <para><b>Backward-compatibility rule (§6.3, the epoch compat trap; A6 SSRF hardening).</b> The key
    /// epoch, the revocation list, and the external-container set are folded into the signed message
    /// <b>only when present</b> — a non-zero <paramref name="epoch"/>, a non-empty <paramref name="revoked"/>
    /// list, or a non-empty <paramref name="externalContainers"/> set. When all three are neutral (epoch 0,
    /// no revocations, no external containers) the message is exactly <c>UTF-8(JSON(files))</c>,
    /// byte-identical to what v1 and every earlier envelope signed, so every already-shipped bundle still
    /// verifies. Each present segment is appended under a unit-separator (U+001F, which never appears in the
    /// JSON), so an attacker cannot lower the epoch, strip/forge a revocation, or repoint a container's
    /// <c>DownloadUrl</c>/hash/membership without invalidating the signature. A v1 bundle is always treated
    /// as epoch 0, no revocations, no external containers.</para>
    /// </summary>
    public static byte[] ComputeSignedBytes(
        IReadOnlyList<ManifestFileEntry> files, int epoch, IReadOnlyList<string> revoked,
        IReadOnlyList<ExternalContainerInfo>? externalContainers)
    {
        var filesJson = JsonSerializer.Serialize(
            files, IntegrityEnvelopeJsonContext.Default.IReadOnlyListManifestFileEntry);

        var hasEpochOrRevoked = epoch != 0 || revoked is { Count: > 0 };
        // The container canonical form is empty for a null/empty set, so a container-free bundle appends
        // nothing — the exact property that keeps every already-shipped bundle byte-identical here.
        var containersCanonical = CanonicalizeExternalContainers(externalContainers);
        var hasContainers = containersCanonical.Length > 0;

        // Neutral (epoch 0, no revocations, no external containers) → the exact legacy files-only bytes.
        // This is the property that keeps v1 and every earlier envelope verifiable.
        if (!hasEpochOrRevoked && !hasContainers)
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

        // External containers, when present, are bound under their own separated, length-prefixed segment
        // (A6): the whole ExternalContainerInfo set — download URL, whole-file hash, and package membership —
        // is cryptographically covered, so a tampered bundle cannot repoint a container download at an
        // internal host (SSRF) or swap its hash without breaking the signature. Appended AFTER the
        // epoch/revocation segment; a container-free bundle appends nothing here and stays byte-identical.
        if (hasContainers)
            sb.Append('').Append("extcontainers=").Append(containersCanonical);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// The canonical, injective, order-independent string form of an external-container set (A6). This is
    /// the exact representation folded into the signed message by
    /// <see cref="ComputeSignedBytes(IReadOnlyList{ManifestFileEntry}, int, IReadOnlyList{string}, IReadOnlyList{ExternalContainerInfo})"/>
    /// and the value <see cref="SignedPayloadTocVerifier"/> compares for the INT013 binding. A null or empty
    /// set yields the empty string, so a bundle with no external containers appends nothing to the signed
    /// message — the backward-compatibility property that keeps every already-shipped bundle's signed bytes
    /// byte-identical.
    ///
    /// <para><b>Determinism:</b> containers are ordered by <see cref="ExternalContainerInfo.Id"/> and each
    /// container's package ids by value (ordinal), so an equivalent-but-reordered set canonicalizes
    /// identically (reordering the download list is benign, not tamper). <b>Injectivity:</b> every string
    /// field is length-prefixed (<c>len:value;</c>) with an element count, so no crafted field value
    /// containing a separator can make two distinct sets collide — the same non-injective-join trap the
    /// revocation-list serialization documents.</para>
    /// </summary>
    public static string CanonicalizeExternalContainers(IReadOnlyList<ExternalContainerInfo>? containers)
    {
        if (containers is null || containers.Count == 0)
            return string.Empty;

        var sorted = new ExternalContainerInfo[containers.Count];
        for (var i = 0; i < containers.Count; i++)
            sorted[i] = containers[i];
        Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Id, b.Id));

        var sb = new StringBuilder();
        sb.Append(sorted.Length).Append(';');
        foreach (var c in sorted)
        {
            AppendField(sb, c.Id);
            AppendField(sb, c.DownloadUrl);
            AppendField(sb, c.Sha256);
            AppendField(sb, c.FileName);

            var pkgIds = c.PackageIds ?? [];
            var sortedPkgs = new string[pkgIds.Length];
            Array.Copy(pkgIds, sortedPkgs, pkgIds.Length);
            Array.Sort(sortedPkgs, StringComparer.Ordinal);

            sb.Append(sortedPkgs.Length).Append(';');
            foreach (var pkg in sortedPkgs)
                AppendField(sb, pkg);
        }

        return sb.ToString();

        // len:value; — the length prefix pins the field boundary regardless of what the value contains,
        // so the encoding is injective (two distinct field tuples can never produce the same string).
        static void AppendField(StringBuilder sb, string? value)
        {
            value ??= string.Empty;
            sb.Append(value.Length).Append(':').Append(value).Append(';');
        }
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
        IReadOnlyList<ManifestFileEntry> files, IReadOnlyList<ECDsa> keys, int epoch, IReadOnlyList<string> revoked) =>
        Sign(files, keys, epoch, revoked, externalContainers: null);

    /// <summary>
    /// Builds and signs a v2 envelope for the supplied file entries using one or more keys, additionally
    /// binding the external-container set (A6) into the signed message. Every key signs the identical
    /// message — <c>SHA-256</c> of
    /// <see cref="ComputeSignedBytes(IReadOnlyList{ManifestFileEntry}, int, IReadOnlyList{string}, IReadOnlyList{ExternalContainerInfo})"/> —
    /// and the container set is stored on the envelope so the verifier reproduces the identical signed bytes.
    /// A null or empty <paramref name="externalContainers"/> set signs the byte-identical container-free
    /// message and leaves the envelope's container field null. The caller owns each key's lifetime.
    /// </summary>
    public static ManifestSignatureEnvelope Sign(
        IReadOnlyList<ManifestFileEntry> files, IReadOnlyList<ECDsa> keys, int epoch, IReadOnlyList<string> revoked,
        IReadOnlyList<ExternalContainerInfo>? externalContainers)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(revoked);
        if (keys.Count == 0)
            throw new ArgumentException("At least one signing key is required.", nameof(keys));

        var hash = SHA256.HashData(ComputeSignedBytes(files, epoch, revoked, externalContainers));

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
                // Low-S canonicalization: CNG emits the malleable high-S twin about half the time,
                // and the verifier rejects it — FalkForge never emits a non-canonical signature.
                Signature = Convert.ToBase64String(EcdsaLowS.Canonicalize(key.SignHash(hash)))
            });
        }

        return new ManifestSignatureEnvelope
        {
            Version = CurrentVersion,
            Algorithm = AlgorithmId,
            Files = files,
            Signatures = signatures,
            Epoch = epoch,
            Revoked = revoked,
            // Normalize empty → null so a container-free envelope's wire form is byte-identical to before
            // (the field is omitted entirely under WhenWritingNull).
            ExternalContainers = externalContainers is { Count: > 0 } ? externalContainers : null
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
    /// the accepted signature on success, and optionally enforces a local revocation list (§6.3 step 3).
    ///
    /// <para><b>Revocation ordering.</b> A revoked key is <i>skipped</i>, not fatal: a legitimate
    /// rotation bundle is dual-signed <c>[old, new]</c>, and when the old key has since been revoked the
    /// iteration must continue to the still-good new signature (matching the quorum path's
    /// <c>DropRevoked</c> semantics). Only when <b>no</b> non-revoked trusted signature validates is the
    /// envelope rejected — a bundle carrying solely revoked trusted signatures still fails INT001.</para>
    /// </summary>
    /// <param name="envelope">The parsed integrity envelope.</param>
    /// <param name="trustedFingerprints">The pinned trusted-key set (empty = consistency-only).</param>
    /// <param name="revokedFingerprints">
    /// Fingerprints recorded as locally revoked; signatures from these keys never match. Null/empty
    /// means no revocations are enforced.
    /// </param>
    /// <param name="pqPolicy">
    /// The post-quantum companion policy (PQ-hybrid Stage 1, §2.2). Null (the default) or an empty
    /// companion map keeps verification bit-for-bit as before. When a trusted classical fingerprint has
    /// a pinned ML-DSA companion, the envelope must ALSO carry a matching, verifying ML-DSA entry for
    /// the classical signature to count — a stripped/wrong/invalid companion fails with INT011 on a
    /// capable OS, and falls back to classical-with-loud-log on an OS that cannot verify ML-DSA.
    /// </param>
    /// <returns>
    /// Success carrying the accepted signature's fingerprint (uppercase hex). INT003 when the envelope
    /// carries no usable signatures. INT011 when the only trusted classical match is a hybrid-pinned key
    /// whose post-quantum companion is missing or invalid. INT001 when no non-revoked signature both
    /// matches a trusted fingerprint and verifies.
    /// </returns>
    public static Result<string> MatchTrustedSignature(
        ManifestSignatureEnvelope envelope,
        IReadOnlySet<string> trustedFingerprints,
        IReadOnlySet<string>? revokedFingerprints = null,
        PqCompanionPolicy? pqPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(trustedFingerprints);

        var signatures = envelope.Signatures;
        if (signatures.Count == 0)
            return Result<string>.Failure(ErrorKind.IntegrityError,
                "INT003: Manifest integrity envelope carries no signatures.");

        // The signed message covers the files and, when present, the epoch + revocation list (§6.3) and the
        // external-container set (A6), so compute the hash once for all entries. Neutral values reproduce the
        // legacy files-only bytes, keeping v1 and every earlier envelope verifiable. The raw message is kept
        // alongside the hash: ML-DSA companion verification is over the message itself (pure ML-DSA, no pre-hash).
        var message = ComputeSignedBytes(
            envelope.Files, envelope.Epoch, envelope.Revoked, envelope.ExternalContainers);
        var hash = SHA256.HashData(message);
        var haveTrustSet = trustedFingerprints.Count > 0;
        var sawRevoked = false;
        var sawNonCanonical = false;
        var sawCompanionFailure = false;

        // PQ side map (only built when companions are pinned): honest ML-DSA entries indexed by their
        // RE-DERIVED fingerprint. These entries are never matched against the trust set themselves —
        // they exist solely as companions consulted after a classical entry verifies.
        var pqEntries = PqCompanionVerifier.IndexPqEntries(signatures, pqPolicy);

        foreach (var entry in signatures)
        {
            // Algorithm dispatch (PQ-hybrid Stage 1): this loop is the classical ECDSA-P256 path.
            // ML-DSA entries live in the side map above; entries with an unknown algorithm are
            // skipped (forward compatibility with future algorithms). An absent algorithm field is
            // ECDSA-P256 — exactly what every pre-hybrid envelope meant.
            if (!PqCompanionVerifier.IsClassicalEntry(entry))
                continue;

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

            // (b') A locally-revoked key never matches — but keep iterating: a dual-signed
            // rotation bundle may carry a still-good signature after the revoked one.
            if (revokedFingerprints is not null && revokedFingerprints.Contains(actualFingerprint))
            {
                sawRevoked = true;
                continue;
            }

            // (b) The key must be pinned. With no baked set, this check is bypassed (consistency-only).
            if (haveTrustSet && !trustedFingerprints.Contains(actualFingerprint))
                continue;

            // (b'') Anti-malleability (low-S enforcement): for a valid ECDSA signature (r, s) the twin
            // (r, n − s) is ALSO cryptographically valid over the same message, and VerifyHash accepts
            // both. FalkForge only ever emits low-S signatures, so a high-S entry is a malleated or
            // foreign artifact and must never count as valid — even though the crypto check below would
            // pass it. Ordered after (a)/(b')/(b) so the fingerprint, revocation, and trust behaviors
            // are unchanged. Any non-64-byte signature is non-canonical too (P-256 only, fail closed).
            if (!EcdsaLowS.IsCanonical(signatureBytes))
            {
                sawNonCanonical = true;
                continue;
            }

            // (c) The signature must cryptographically verify over the signed message.
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
                if (ecdsa.VerifyHash(hash, signatureBytes))
                {
                    // (d) PQ companion rule (§2.2): a hybrid-pinned key counts only when its pinned
                    // ML-DSA companion is present and verifies (or the OS cannot verify ML-DSA — the
                    // logged classical-fallback branch). Keep iterating on failure: another trusted
                    // signature may still satisfy, same continue-shape as the revoked-key skip.
                    if (!PqCompanionVerifier.SatisfiesPqCompanion(actualFingerprint, pqPolicy, pqEntries, message))
                    {
                        sawCompanionFailure = true;
                        continue;
                    }

                    return Result<string>.Success(actualFingerprint);
                }
            }
            catch (CryptographicException)
            {
                // Import/verify failed for this entry — fall through to try the next signature.
            }
        }

        // Most specific failure first: a companion failure means a TRUSTED classical signature DID
        // verify but its pinned post-quantum companion was stripped, wrong, or invalid — the exact
        // downgrade a quantum-capable forger of ECDSA would attempt. Fail loud and specific.
        if (sawCompanionFailure)
            return Result<string>.Failure(ErrorKind.IntegrityError,
                "INT011: A trusted key is pinned as hybrid (post-quantum companion required), but the " +
                "envelope's ML-DSA companion signature is missing, from the wrong key, or failed to " +
                "verify. Refusing to accept the classical signature alone — a stripped or forged " +
                "post-quantum companion is treated as a downgrade attack.");

        if (sawRevoked)
            return Result<string>.Failure(ErrorKind.IntegrityError,
                "INT001: The bundle's signature is from a key that has been revoked on this machine. " +
                "Refusing to extract or execute a payload signed by a revoked publisher key.");

        if (sawNonCanonical)
            return Result<string>.Failure(ErrorKind.IntegrityError,
                "INT001: A signature on the manifest is not in canonical low-S form. FalkForge only " +
                "emits low-S ECDSA signatures, so this is a malleated or foreign signature — refusing " +
                "to treat it as valid.");

        return Result<string>.Failure(ErrorKind.IntegrityError,
            "INT001: No trusted signature validates the manifest. The bundle may have been tampered " +
            "with or signed by an untrusted publisher.");
    }

    /// <summary>
    /// Collects EVERY valid, trusted, DISTINCT signature in the envelope, resolving each accepted
    /// fingerprint to its pinned role via <paramref name="roleOf"/> (C19 §6.1). Unlike the C14 first-wins
    /// <see cref="MatchTrustedSignature"/>, this never short-circuits — it returns the full evidence for the
    /// quorum evaluator (<see cref="QuorumEvaluator"/>) to weigh against an operation's policy. Distinct-key
    /// dedup (by uppercase-hex fingerprint) is the determinism the quorum guarantee hinges on: a bundle that
    /// repeats one key contributes exactly one member.
    ///
    /// <para>Per-entry checks mirror <see cref="MatchTrustedSignature"/>: a lying fingerprint (one that does
    /// not re-derive to its own key) or an untrusted key is skipped, never counted. Roles are resolved from
    /// the pinned trusted set, never from the bundle, so a key can never assert its own privilege.</para>
    /// </summary>
    /// <param name="envelope">The parsed integrity envelope.</param>
    /// <param name="trustedFingerprints">
    /// The pinned trusted-key set. Quorum requires named roles, which require a pinned set — an empty set
    /// therefore collects nothing (the require-signed path already fails closed with INT009 upstream).
    /// </param>
    /// <param name="roleOf">Resolves an accepted fingerprint to its pinned role(s).</param>
    /// <param name="pqPolicy">
    /// The post-quantum companion policy (PQ-hybrid Stage 1, §2.2). Null or an empty companion map keeps
    /// collection bit-for-bit as before. A hybrid-pinned classical signature whose ML-DSA companion is
    /// missing or invalid is NOT collected (it contributes nothing toward any quorum); ML-DSA entries are
    /// never collected as members themselves — a hybrid pair is ONE signer, so the C19 distinct-key
    /// guarantee is preserved with zero changes to the quorum evaluator.
    /// </param>
    /// <returns>
    /// Success carrying the distinct trusted signatures (possibly empty when none are trusted). INT003 when
    /// the envelope carries no signatures at all.
    /// </returns>
    public static Result<IReadOnlyList<TrustedSignature>> CollectTrustedSignatures(
        ManifestSignatureEnvelope envelope,
        IReadOnlySet<string> trustedFingerprints,
        Func<string, TrustRole> roleOf,
        PqCompanionPolicy? pqPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(trustedFingerprints);
        ArgumentNullException.ThrowIfNull(roleOf);

        var signatures = envelope.Signatures;
        if (signatures.Count == 0)
            return Result<IReadOnlyList<TrustedSignature>>.Failure(ErrorKind.IntegrityError,
                "INT003: Manifest integrity envelope carries no signatures.");

        // Same signed message as MatchTrustedSignature — files plus, when present, epoch + revocations and
        // the external-container set. The raw message is kept for ML-DSA companion verification (no pre-hash).
        var message = ComputeSignedBytes(
            envelope.Files, envelope.Epoch, envelope.Revoked, envelope.ExternalContainers);
        var hash = SHA256.HashData(message);

        // PQ side map, mirroring MatchTrustedSignature: companions consulted after a classical entry
        // verifies, never independent quorum members.
        var pqEntries = PqCompanionVerifier.IndexPqEntries(signatures, pqPolicy);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var collected = new List<TrustedSignature>(signatures.Count);

        foreach (var entry in signatures)
        {
            // Algorithm dispatch (PQ-hybrid Stage 1): only classical ECDSA-P256 entries are quorum
            // candidates; ML-DSA entries live in the side map, unknown algorithms are skipped.
            if (!PqCompanionVerifier.IsClassicalEntry(entry))
                continue;

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
                continue;
            }

            // (a) The declared fingerprint must equal its own key's fingerprint (no lying fingerprint).
            var actualFingerprint = ComputeFingerprint(spki);
            if (!string.Equals(actualFingerprint, entry.Fingerprint, StringComparison.OrdinalIgnoreCase))
                continue;

            // (b) The key must be pinned. Quorum requires a named role, which requires a pinned set.
            if (!trustedFingerprints.Contains(actualFingerprint))
                continue;

            // (b'') Anti-malleability (low-S enforcement), mirroring MatchTrustedSignature: a high-S
            // signature is a malleated or foreign artifact and never counts toward a quorum.
            if (!EcdsaLowS.IsCanonical(signatureBytes))
                continue;

            // (c) The signature must cryptographically verify; first distinct occurrence only.
            // (d) PQ companion rule (§2.2): a hybrid-pinned key contributes toward a quorum only
            // when its pinned ML-DSA companion is present and verifies (or the OS cannot verify
            // ML-DSA — the logged classical-fallback branch).
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
                if (ecdsa.VerifyHash(hash, signatureBytes)
                    && PqCompanionVerifier.SatisfiesPqCompanion(actualFingerprint, pqPolicy, pqEntries, message)
                    && seen.Add(actualFingerprint))
                {
                    collected.Add(new TrustedSignature(actualFingerprint, roleOf(actualFingerprint)));
                }
            }
            catch (CryptographicException)
            {
                // Import/verify failed for this entry — try the next signature.
            }
        }

        return Result<IReadOnlyList<TrustedSignature>>.Success(collected);
    }
}
