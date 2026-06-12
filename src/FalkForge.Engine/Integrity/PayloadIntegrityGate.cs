namespace FalkForge.Engine.Integrity;

using System.Globalization;
using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Runtime gate that proves payload integrity before any package executes.
///
/// <para><b>What the signature actually proves.</b> The bundle manifest carries an ECDSA
/// signature envelope over the per-package SHA-256 hashes, and the envelope embeds the
/// public half of the signing key. The cache layer verifies each payload's bytes against
/// its <see cref="PackageInfo.Sha256Hash"/>; this gate proves those hashes are the ones
/// covered by the signature, and that the signed set covers every package that will run.
/// Together they guarantee <b>internal consistency</b>: the payloads, their manifest
/// hashes, and the signature all agree.</para>
///
/// <para><b>What it does NOT prove by default.</b> Because the verifying key is carried
/// <i>inside</i> the envelope (ephemeral / self-describing mode), an attacker who rewrites
/// the whole bundle can recompute the hashes, re-sign the file list with their <i>own</i>
/// ECDSA key, and embed their own public key — verification then passes. So the default
/// mode is <b>tamper-evidence in transit / casual-tamper detection, not authorship</b>: it
/// detects an attacker who flips bytes in a payload or manifest without re-signing, but not
/// one who fully re-authors the bundle. See <c>docs/provenance.md §3</c>.</para>
///
/// <para><b>Authorship requires an out-of-band pin.</b> A host embedding the engine can
/// pass an <c>expectedPublisherKeyFingerprint</c> (SHA-256 of the signer's
/// SubjectPublicKeyInfo, uppercase hex) obtained through a channel the attacker cannot
/// rewrite — the host's own binary/config, not the bundle. When supplied, the gate requires
/// the envelope's embedded public key to match the pinned fingerprint, so a re-signing
/// attacker (different key) is rejected. This is the practical seam until the signed-feed
/// (TUF-lite) phase carries publisher keys end-to-end.</para>
///
/// <para>Verification is independent of Authenticode and uses only built-in .NET
/// cryptography, so the NativeAOT engine needs no external tool. An unsigned manifest passes
/// through unchanged for backward compatibility (see <c>docs/provenance.md §3</c>).</para>
/// </summary>
internal static class PayloadIntegrityGate
{
    /// <summary>
    /// Verifies the manifest's integrity envelope, if present.
    /// </summary>
    /// <param name="manifest">The manifest whose signature envelope (if any) is verified.</param>
    /// <param name="expectedPublisherKeyFingerprint">
    /// Optional out-of-band pin: the SHA-256 fingerprint (uppercase hex, no separators) of the
    /// expected signer's SubjectPublicKeyInfo public key. When non-null, the envelope's embedded
    /// public key must hash to this value or verification fails with a
    /// <see cref="ErrorKind.SecurityError"/>. This is the only mechanism that proves
    /// <i>authorship</i> in self-describing-key mode; it must come from a channel the attacker
    /// cannot rewrite (the host's own binary/config, never the bundle). When null (default),
    /// the gate proves internal consistency only — not authorship.
    /// </param>
    /// <returns>
    /// Success when the manifest is unsigned (backward compatible) or when the signature is
    /// valid, every signed entry binds to a manifest package whose hash matches, every manifest
    /// package is covered by the signed set, and (when pinned) the embedded key matches the
    /// pin. Returns a <see cref="ErrorKind.SecurityError"/> otherwise so the pipeline aborts.
    /// </returns>
    internal static Result<Unit> Verify(
        InstallerManifest manifest,
        string? expectedPublisherKeyFingerprint = null)
    {
        if (manifest.ManifestSignature is null)
            return Result<Unit>.Success(default);

        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
        if (envelope is null)
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "INT003: Failed to parse manifest integrity envelope.");

        if (string.IsNullOrEmpty(envelope.PublicKey) || string.IsNullOrEmpty(envelope.Signature))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "INT003: Manifest integrity envelope is missing the public key or signature.");

        if (!IntegrityEnvelopeCodec.VerifySignature(envelope))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "INT001: Manifest integrity signature verification failed. The installer may have been tampered with.");

        // Out-of-band publisher pin (authorship). Without this, a re-signing attacker with their
        // own key passes the checks above; the pin binds the install to a specific publisher key
        // supplied through a channel the attacker cannot rewrite.
        if (!string.IsNullOrEmpty(expectedPublisherKeyFingerprint))
        {
            var pinResult = VerifyPublisherPin(envelope.PublicKey, expectedPublisherKeyFingerprint);
            if (pinResult.IsFailure)
                return pinResult;
        }

        // Direction 1 — signed → manifest: every signed entry must bind to a manifest package
        // whose hash matches the signed hash the cache enforces against payload bytes.
        foreach (var entry in envelope.Files)
        {
            if (string.IsNullOrEmpty(entry.Name))
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    "INT003: Manifest integrity envelope has an entry with an empty name.");

            var package = FindPackage(manifest, entry.Name);
            if (package is null)
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    $"INT002: Signed integrity entry '{entry.Name}' has no matching package in the manifest.");

            if (!string.Equals(package.Sha256Hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    $"INT002: Integrity hash mismatch for '{entry.Name}'. Signed {entry.Sha256}, manifest has {package.Sha256Hash}.");
        }

        // Direction 2 — manifest → signed (set coverage): once a manifest is signed, EVERY
        // package that will execute must be in the signed set. Otherwise an attacker could
        // append an unsigned package to a validly signed bundle and have it run alongside the
        // signed ones. An unsigned-extra package is a SecurityError, not a silent pass.
        foreach (var package in manifest.Packages)
        {
            if (!IsInSignedSet(envelope, package.Id))
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    $"INT004: Manifest package '{package.Id}' is not covered by the integrity signature. " +
                    "Every package in a signed manifest must be signed.");
        }

        return Result<Unit>.Success(default);
    }

    private static Result<Unit> VerifyPublisherPin(string publicKeyBase64, string expectedFingerprint)
    {
        byte[] spki;
        try
        {
            spki = Convert.FromBase64String(publicKeyBase64);
        }
        catch (FormatException)
        {
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "INT003: Manifest integrity envelope public key is not valid base64.");
        }

        var actualFingerprint = Convert.ToHexString(SHA256.HashData(spki));

        // Normalize the expected pin: tolerate lowercase and common separators (':' / '-' / ' ')
        // so hosts can paste fingerprints in the usual display formats.
        var normalizedExpected = NormalizeFingerprint(expectedFingerprint);

        if (!string.Equals(actualFingerprint, normalizedExpected, StringComparison.OrdinalIgnoreCase))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                string.Format(CultureInfo.InvariantCulture,
                    "INT005: Publisher key pin mismatch. Expected key fingerprint {0}, bundle was signed by {1}. " +
                    "The bundle may have been re-signed by an untrusted publisher.",
                    normalizedExpected, actualFingerprint));

        return Result<Unit>.Success(default);
    }

    private static string NormalizeFingerprint(string fingerprint)
    {
        Span<char> buffer = fingerprint.Length <= 128 ? stackalloc char[fingerprint.Length] : new char[fingerprint.Length];
        var len = 0;
        foreach (var c in fingerprint)
        {
            if (c is ':' or '-' or ' ')
                continue;
            buffer[len++] = char.ToUpperInvariant(c);
        }

        return new string(buffer[..len]);
    }

    private static bool IsInSignedSet(ManifestSignatureEnvelope envelope, string packageId)
    {
        foreach (var entry in envelope.Files)
        {
            if (string.Equals(entry.Name, packageId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static PackageInfo? FindPackage(InstallerManifest manifest, string id)
    {
        foreach (var package in manifest.Packages)
        {
            if (string.Equals(package.Id, id, StringComparison.Ordinal))
                return package;
        }

        return null;
    }
}
