using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Models;
using FalkForge.Signing;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Build-time payload signer using pure .NET ECDSA (P-256). Produces a
/// <see cref="ManifestSignatureEnvelope"/> the engine verifies before executing any
/// payload, independent of Authenticode and of the external <c>sigil</c> CLI.
///
/// <para>Signing is delegated to one or more <see cref="ISignatureProvider"/> backends (C17). The default
/// set is derived from the integrity config: a <see cref="PemSignatureProvider"/> per configured PEM key
/// (stable public key = authorship proof across builds), plus any custom
/// <see cref="IntegrityConfiguration.SignatureProviders"/>; with no key and no provider configured a single
/// <see cref="EphemeralSignatureProvider"/> gives zero-config tamper detection. Every provider signs the
/// identical canonical message and contributes one signature entry, so the historical single-key,
/// multi-key, and ephemeral behaviors are all preserved byte-for-byte in shape.</para>
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
    /// Signs the supplied payload hashes and returns the canonical envelope JSON to embed in the manifest.
    /// Synchronous bridge over <see cref="SignAsync"/> for the sync build pipeline
    /// (<c>BundleCompiler.Compile</c>): the built-in providers complete synchronously, so this never blocks
    /// a thread. A genuinely asynchronous provider (e.g. a future remote signing service) must be driven
    /// through <see cref="SignAsync"/> on an async build path — here it fails loud rather than block.
    /// </summary>
    internal static Result<string> Sign(
        IReadOnlyList<PayloadHashEntry> entries,
        IntegrityConfiguration? config)
    {
        var task = SignAsync(entries, config, CancellationToken.None);

        // Built-in providers (PEM/ephemeral) always complete synchronously and successfully — the common,
        // no-block path.
        if (task.IsCompletedSuccessfully)
            return task.Result;

        // A completed-but-faulted local sign: observe the exception synchronously (already completed, so no
        // thread blocks) via AsTask so it surfaces rather than being swallowed.
        if (task.IsCompleted)
            return task.AsTask().GetAwaiter().GetResult();

        // Still pending → a genuinely asynchronous provider was configured. Fail loud instead of blocking a
        // thread; such a backend must be driven through SignAsync on an async build pipeline.
        return Result<string>.Failure(ErrorKind.SecurityError,
            "SGN010: An asynchronous signature provider was configured; use the async signing pipeline.");
    }

    /// <summary>
    /// Signs the supplied payload hashes with every configured <see cref="ISignatureProvider"/> and returns
    /// the canonical envelope JSON. Returns the provider's error (e.g. SGN002 for a missing key file) on the
    /// first failure. This is the async seam a remote signing backend plugs into.
    /// </summary>
    internal static async ValueTask<Result<string>> SignAsync(
        IReadOnlyList<PayloadHashEntry> entries,
        IntegrityConfiguration? config,
        CancellationToken cancellationToken = default)
    {
        var files = new List<ManifestFileEntry>(entries.Count);
        foreach (var entry in entries)
            files.Add(new ManifestFileEntry { Name = entry.PackageId, Sha256 = entry.Sha256 });

        // Fold the publisher's epoch + declared revocations into the signed message (C14 Stage 2).
        // Neutral values (epoch 0, no revocations) reproduce the legacy files-only signed bytes, so an
        // unchanged config keeps producing byte-identical signed bytes across v1/v2.
        var epoch = config?.Epoch ?? 0;
        IReadOnlyList<string> revoked = config?.RevokedFingerprints ?? [];
        var message = IntegrityEnvelopeCodec.ComputeSignedBytes(files, epoch, revoked);

        var providers = BuildProviders(config);
        // PQ-hybrid Stage 1: classical entries are ordered before post-quantum entries regardless of
        // provider declaration order, so the runtime's first-wins verify loop meets the cheap ECDSA
        // path first. Two lists + concat keeps the classical wire shape untouched.
        var classicalSignatures = new List<SignatureEntry>(providers.Count);
        var pqSignatures = new List<SignatureEntry>();
        foreach (var provider in providers)
        {
            var result = await provider.SignAsync(message, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
                return Result<string>.Failure(result.Error);

            var signature = result.Value;
            if (string.Equals(signature.Algorithm, SignatureAlgorithms.EcdsaP256, StringComparison.Ordinal))
            {
                classicalSignatures.Add(new SignatureEntry
                {
                    KeyId = signature.KeyId,
                    Fingerprint = IntegrityEnvelopeCodec.ComputeFingerprint(signature.SubjectPublicKeyInfo),
                    PublicKey = Convert.ToBase64String(signature.SubjectPublicKeyInfo),
                    // Defense-in-depth low-S canonicalization: the built-in providers already canonicalize,
                    // but a CUSTOM ISignatureProvider may hand back a malleable high-S signature the verifier
                    // would reject. Canonicalizing at the envelope-assembly chokepoint guarantees FalkForge
                    // never emits a non-canonical signature regardless of backend.
                    Signature = Convert.ToBase64String(EcdsaLowS.Canonicalize(signature.Signature))
                    // Algorithm stays null: an absent field IS the classical algorithm on the wire,
                    // keeping every classical entry byte-identical to pre-hybrid envelopes.
                });
            }
            else
            {
                pqSignatures.Add(new SignatureEntry
                {
                    KeyId = signature.KeyId,
                    Fingerprint = IntegrityEnvelopeCodec.ComputeFingerprint(signature.SubjectPublicKeyInfo),
                    PublicKey = Convert.ToBase64String(signature.SubjectPublicKeyInfo),
                    // Emitted byte-verbatim: low-S canonicalization is an ECDSA-P256 concept; an ML-DSA
                    // signature is the raw FIPS 204 signature over the raw message under the manifest
                    // context, and must not be run through any classical normalization.
                    Signature = Convert.ToBase64String(signature.Signature),
                    Algorithm = signature.Algorithm
                });
            }
        }

        var signatures = new List<SignatureEntry>(classicalSignatures.Count + pqSignatures.Count);
        signatures.AddRange(classicalSignatures);
        signatures.AddRange(pqSignatures);

        var envelope = new ManifestSignatureEnvelope
        {
            Version = IntegrityEnvelopeCodec.CurrentVersion,
            Algorithm = IntegrityEnvelopeCodec.AlgorithmId,
            Files = files,
            Signatures = signatures,
            Epoch = epoch,
            Revoked = revoked
        };

        return IntegrityEnvelopeCodec.Serialize(envelope);
    }

    /// <summary>
    /// Resolves the signature providers for this build. A <see cref="PemSignatureProvider"/> is created for
    /// each configured PEM key — <see cref="IntegrityConfiguration.SigningKeyPaths"/> (rotation-safe
    /// dual-sign) preferred, else the single <see cref="IntegrityConfiguration.SigningKeyPath"/> — in
    /// declaration order, then every custom <see cref="IntegrityConfiguration.SignatureProviders"/> entry is
    /// appended. When nothing is configured a single <see cref="EphemeralSignatureProvider"/> is used for
    /// zero-config tamper detection. The ordering preserves the historical signature-entry order.
    /// </summary>
    private static IReadOnlyList<ISignatureProvider> BuildProviders(IntegrityConfiguration? config)
    {
        var providers = new List<ISignatureProvider>();

        foreach (var keyPath in ResolveKeyPaths(config))
            providers.Add(new PemSignatureProvider(keyPath));

        if (config?.SignatureProviders is { Count: > 0 } custom)
            providers.AddRange(custom);

        if (providers.Count == 0)
        {
            providers.Add(new EphemeralSignatureProvider());
            // PQ-hybrid Stage 1 (human decision §8.7): the zero-config build is hybrid too, so the dev
            // loop exercises the same envelope shape production uses. Gated on OS capability — the
            // ephemeral path is the zero-config fallback and must keep working on build machines whose
            // OS cannot do ML-DSA (a CONFIGURED MLDsaPemSignatureProvider still fails loud there).
            if (System.Security.Cryptography.MLDsa.IsSupported)
                providers.Add(new EphemeralMLDsaSignatureProvider());
        }

        return providers;
    }

    private static IReadOnlyList<string> ResolveKeyPaths(IntegrityConfiguration? config)
    {
        if (config?.SigningKeyPaths is { Count: > 0 } multi)
            return multi;
        if (!string.IsNullOrEmpty(config?.SigningKeyPath))
            return [config.SigningKeyPath];
        return [];
    }
}
