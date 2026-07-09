namespace FalkForge.Engine.Integrity;

using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Threading;
using FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// The process-wide trust anchor: the effective set of trusted publisher-key fingerprints the engine
/// honors. It is the MSBuild-baked set (<see cref="BakedTrustedKeys"/>) UNIONed with any keys a publisher
/// registers from their own compiled bootstrap code before the first verification runs. This is the
/// code-path counterpart to the <c>-p:FalkForgeTrustedKey</c> build parameter (C18) — additive, never a
/// replacement: baked keys are always honored.
///
/// <para><b>Security boundary (this is the whole point).</b> Registration is reachable ONLY from compiled
/// bootstrap code — the engine's <c>Program.ConfigureTrust</c> hook a publisher implements when they
/// rebuild the engine. It is NEVER fed from a bundle, manifest, downloaded update, or any network/file
/// input the installer processes; doing so would reopen the trust-anchor hole C14 closed (a self-describing
/// key that grants its own trust). The verifiers read only the frozen effective set and derive trust
/// exclusively from it — key material embedded in a bundle is used to check a signature, never to decide
/// whether that signature is trusted.</para>
///
/// <para><b>Freeze-once.</b> The effective set is computed once, on the first read of
/// <see cref="EffectiveFingerprints"/> (which every verification path performs), and is immutable
/// thereafter. A registration attempt after the freeze throws (<see cref="InvalidOperationException"/>,
/// fail loud) so trust can never be widened after a verification has begun. Register in the bootstrap hook,
/// which runs before any bundle is touched.</para>
///
/// <para><b>Empty set.</b> With no baked key and no code registration the effective set is empty, which
/// keeps the exact C14 semantics: consistency-only on the fresh-install path, fail-closed (INT009) on the
/// require-signed update path.</para>
///
/// <para>AOT-safe: no reflection; the set is a <see cref="FrozenSet{T}"/> built once at bootstrap.</para>
/// </summary>
public static class EngineTrustAnchor
{
    /// <summary>The number of hex characters in a SHA-256 SubjectPublicKeyInfo fingerprint.</summary>
    private const int FingerprintHexLength = 64; // SHA-256 = 32 bytes = 64 hex chars

    private static readonly object Gate = new();

    // Staging area for code-registered fingerprints, mutable only until the first freeze. Uppercase hex,
    // no separators — the canonical form the verifiers compare against (OrdinalIgnoreCase).
    private static readonly HashSet<string> CodeRegistered = new(StringComparer.OrdinalIgnoreCase);

    // Null until the effective set is frozen on first read. Once non-null, registration is closed.
    private static FrozenSet<string>? _effective;

    /// <summary>
    /// The frozen effective trusted set: the baked set (<see cref="BakedTrustedKeys.Fingerprints"/>) unioned
    /// with all code-registered fingerprints. The first read freezes the set; every subsequent read returns
    /// the same instance. Never null (empty when nothing is trusted). All verification reads this.
    /// </summary>
    public static FrozenSet<string> EffectiveFingerprints
    {
        get
        {
            var current = Volatile.Read(ref _effective);
            if (current is not null)
                return current;

            lock (Gate)
            {
                if (_effective is not null)
                    return _effective;

                var union = new HashSet<string>(BakedTrustedKeys.Fingerprints, StringComparer.OrdinalIgnoreCase);
                union.UnionWith(CodeRegistered);
                var frozen = union.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                Volatile.Write(ref _effective, frozen);
                return frozen;
            }
        }
    }

    /// <summary>
    /// True once the effective set has been frozen (the first verification has begun). After this,
    /// registration throws.
    /// </summary>
    public static bool IsFrozen => Volatile.Read(ref _effective) is not null;

    /// <summary>
    /// Registers a trusted publisher key by its SubjectPublicKeyInfo (SPKI) blob — the self-describing,
    /// non-secret public key. The SHA-256 fingerprint is derived exactly as the envelope verifier derives
    /// it (<see cref="IntegrityEnvelopeCodec.ComputeFingerprint"/>), so a bundle signed by this key will be
    /// trusted. Call from the engine bootstrap hook, before any bundle is verified.
    /// </summary>
    /// <param name="subjectPublicKeyInfo">The DER-encoded SubjectPublicKeyInfo of the trusted key.</param>
    /// <exception cref="ArgumentException">The blob is empty.</exception>
    /// <exception cref="InvalidOperationException">The effective set is already frozen.</exception>
    public static void TrustPublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (subjectPublicKeyInfo.IsEmpty)
            throw new ArgumentException("SubjectPublicKeyInfo must not be empty.", nameof(subjectPublicKeyInfo));

        // Derive the fingerprint with the same primitives as IntegrityEnvelopeCodec.ComputeFingerprint
        // (SHA-256 of the SPKI, uppercase hex, no separators) so a code-registered key and a signed
        // envelope agree to the byte. Stack-allocated: SHA-256 is 32 bytes.
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(subjectPublicKeyInfo, hash);
        var fingerprint = Convert.ToHexString(hash);

        RegisterCanonical(fingerprint);
    }

    /// <summary>
    /// Registers a trusted publisher key from a PEM-encoded SubjectPublicKeyInfo (a
    /// <c>-----BEGIN PUBLIC KEY-----</c> block). Convenience over <see cref="TrustPublicKey"/> for keys
    /// distributed as PEM text.
    /// </summary>
    /// <param name="publicKeyPem">The PEM text of the public key (SubjectPublicKeyInfo).</param>
    /// <exception cref="ArgumentException">The PEM is null/empty or not a readable public key.</exception>
    /// <exception cref="InvalidOperationException">The effective set is already frozen.</exception>
    public static void TrustPublicKeyPem(string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);

        byte[] spki;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            spki = ecdsa.ExportSubjectPublicKeyInfo();
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new ArgumentException(
                "The supplied PEM is not a readable public key (SubjectPublicKeyInfo).", nameof(publicKeyPem), ex);
        }

        TrustPublicKey(spki);
    }

    /// <summary>
    /// Registers a trusted publisher-key fingerprint directly (the SHA-256 of a SubjectPublicKeyInfo,
    /// uppercase hex). Separators (spaces, colons, hyphens) and lettercase are normalized, so a
    /// display-formatted fingerprint like <c>A1:B2:...</c> is accepted. Use when you already have the
    /// fingerprint rather than the key.
    /// </summary>
    /// <param name="fingerprint">A 64-hex-character SHA-256 fingerprint, with optional separators.</param>
    /// <exception cref="ArgumentException">Null/whitespace, or not a 64-character hex fingerprint.</exception>
    /// <exception cref="InvalidOperationException">The effective set is already frozen.</exception>
    public static void TrustFingerprint(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        RegisterCanonical(Normalize(fingerprint));
    }

    /// <summary>
    /// Normalizes a fingerprint to canonical form: strips common display separators, uppercases, and
    /// validates it is exactly a 64-character hex SHA-256 fingerprint. Rejects anything else (fail loud) so
    /// a typo cannot be silently truncated into a fingerprint that never matches.
    /// </summary>
    private static string Normalize(string fingerprint)
    {
        Span<char> buffer = stackalloc char[FingerprintHexLength];
        var length = 0;
        foreach (var c in fingerprint)
        {
            if (c is ' ' or ':' or '-' or '\t')
                continue;
            if (!Uri.IsHexDigit(c))
                throw new ArgumentException(
                    $"Fingerprint contains a non-hex character '{c}'. Expected a 64-character hex SHA-256 fingerprint.",
                    nameof(fingerprint));
            if (length == FingerprintHexLength)
                throw new ArgumentException(
                    "Fingerprint is longer than a 64-character hex SHA-256 fingerprint.", nameof(fingerprint));
            buffer[length++] = char.ToUpperInvariant(c);
        }

        if (length != FingerprintHexLength)
            throw new ArgumentException(
                $"Fingerprint must be a 64-character hex SHA-256 value (got {length} hex characters).",
                nameof(fingerprint));

        return new string(buffer);
    }

    private static void RegisterCanonical(string canonicalFingerprint)
    {
        lock (Gate)
        {
            if (_effective is not null)
                throw new InvalidOperationException(
                    "The engine trust anchor is already frozen; trusted keys can only be registered during " +
                    "bootstrap, before the first bundle verification. Register in the Program.ConfigureTrust hook.");

            CodeRegistered.Add(canonicalFingerprint);
        }
    }

    /// <summary>
    /// Test-only: clears all code-registered keys and unfreezes the effective set so each test starts from a
    /// pristine anchor. The engine test assembly runs serially, so this is safe. Never called in production.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            CodeRegistered.Clear();
            _effective = null;
        }
    }
}
