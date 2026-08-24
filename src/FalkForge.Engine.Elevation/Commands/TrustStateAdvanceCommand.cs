namespace FalkForge.Engine.Elevation.Commands;

using System.Text.Json;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Whitelisted elevated command that advances the per-machine anti-downgrade/revocation store (C16). The
/// non-elevated engine cannot write under the restrictive store ACL, so after a fully-verified update apply
/// it forwards the accepted update's publisher-signed installer manifest here; this command — running
/// elevated — verifies that manifest against its OWN baked publisher-key set, takes the epoch and
/// revocations from the VERIFIED envelope, re-hardens the store directory, and writes the store monotonically.
///
/// <para>The epoch and revocations are never read from the wire. A same-user caller on this pipe can no
/// longer name an arbitrary epoch (to jam the anti-downgrade floor and lock out future updates) or an
/// arbitrary revocation (to brick a publisher key): both live inside the ECDSA-signed message, so the
/// companion accepts them only after the signature verifies against a key its baked set trusts. This mirrors
/// the publisher gate <see cref="MsiInstallCommand"/> applies before a SYSTEM MSI install. An unsigned
/// manifest, an old raw-int-format payload, an empty baked set, or a signature from an untrusted key is
/// refused and the store is left untouched (fail closed).</para>
///
/// <para>Monotonicity is still enforced by <see cref="TrustStateStore.Advance"/> (it never lowers the stored
/// epoch and only unions revocations), so even a replayed lower verified epoch cannot roll the store back.</para>
/// </summary>
public sealed class TrustStateAdvanceCommand : IElevatedCommand
{
    private readonly string _storePath;
    private readonly IReadOnlySet<string> _trustedFingerprints;
    private readonly IReadOnlyDictionary<string, TrustRole> _trustedRoles;
    private readonly IReadOnlyDictionary<string, string> _trustedPqCompanions;

    /// <summary>Production ctor: writes the per-machine default store path, verifying against the baked set.</summary>
    public TrustStateAdvanceCommand()
        : this(TrustStateStore.DefaultPath)
    {
    }

    /// <summary>Production/override ctor: writes the supplied store path, verifying against the baked set.</summary>
    public TrustStateAdvanceCommand(string storePath)
        : this(storePath,
            BakedTrustedKeys.Fingerprints, BakedTrustedKeys.Roles, BakedTrustedKeys.PqCompanions)
    {
    }

    // Test seam: the baked publisher-key set this companion independently verifies the manifest against
    // before advancing the store. Production always uses the engine's compile-time BakedTrustedKeys (empty
    // unless the publisher baked a key; with an empty set an advance is refused because authorship cannot be
    // established). A test injects a known trusted set so the require-signed gate can be exercised without a
    // baked build. The injection never WEAKENS the production default: the two public overloads above always
    // pass the baked set.
    internal TrustStateAdvanceCommand(
        string storePath,
        IReadOnlySet<string> trustedFingerprints,
        IReadOnlyDictionary<string, TrustRole> trustedRoles,
        IReadOnlyDictionary<string, string> trustedPqCompanions)
    {
        ArgumentException.ThrowIfNullOrEmpty(storePath);
        _storePath = storePath;
        _trustedFingerprints = trustedFingerprints;
        _trustedRoles = trustedRoles;
        _trustedPqCompanions = trustedPqCompanions;
    }

    public string Name => "TrustStateAdvance";

    public Result<byte[]> Execute(byte[] payload, Action<int>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // New wire format: a magic + version prefix in front of the publisher-signed manifest. An old
        // raw-int-format payload (bare epoch + revocations, no signature) fails this parse and is refused —
        // there is no fallback to the old parser and a missing manifest is never a legacy allow-through.
        if (!TrustAdvancePayload.TryDeserialize(payload, out var manifestJson))
            return Result<byte[]>.Failure(ErrorKind.SecurityError,
                "TrustStateAdvance: malformed or old-format advance payload; refusing to touch the trust store.");

        // Read the REAL stored epoch the store currently holds, using the same read Advance itself uses
        // (LoadForAdvance: absent/malformed → first-run epoch 0 self-heal, unreadable → fail loud). This is
        // load-bearing: the operation is resolved on the UPDATE path (isUpdatePath: true), so a routine
        // post-rotation same-epoch update must see envelope.Epoch == storedEpoch and resolve as Update (one
        // release key). Hardcoding 0 (as the fresh-install MsiInstall gate does) would classify every
        // same-epoch update at a non-zero stored epoch as a KeyChange and wrongly demand a recovery
        // co-signature. A failure here (an unreadable store) aborts with the store untouched.
        var loaded = TrustStateStore.LoadForAdvance(_storePath);
        if (loaded.IsFailure)
            return Result<byte[]>.Failure(loaded.Error);
        var storedEpoch = loaded.Value.Epoch;

        // Independently prove authorship: verify the manifest's signed integrity envelope against this
        // companion's OWN baked publisher-key set on the require-signed UPDATE path. This is what stops a
        // same-user caller from jamming the epoch or injecting a revocation — the caller cannot forge a
        // signature the baked set trusts. On any failure the store is left untouched.
        var verified = VerifyAndResolveAdvance(manifestJson, storedEpoch);
        if (verified.IsFailure)
            return Result<byte[]>.Failure(verified.Error);

        // Re-harden the store directory before writing: create it hardened when absent, or reset a
        // non-conforming (attacker pre-created / loose) directory to the restrictive DACL (anti-squat). Only
        // reached after verification, so a forged/unsigned advance never triggers a filesystem write.
        var dir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(dir))
        {
            var secured = TrustStateStore.EnsureSecuredDirectory(dir);
            if (secured.IsFailure)
                return Result<byte[]>.Failure(secured.Error);
        }

        // Take the epoch + revocations from the VERIFIED envelope, never from the wire. Monotonic + union —
        // Advance never lowers the epoch, so even a replayed lower verified epoch is a no-op, not a rollback.
        var advance = TrustStateStore.Advance(_storePath, verified.Value.Epoch, verified.Value.Revoked);
        if (advance.IsFailure)
            return Result<byte[]>.Failure(advance.Error);

        return Array.Empty<byte>();
    }

    /// <summary>
    /// Verifies the manifest's signed integrity envelope against the baked publisher-key set on the
    /// require-signed update path (anti-downgrade epoch <paramref name="storedEpoch"/>), then re-parses the
    /// verified envelope for the epoch + revocations to persist. Fails closed on every path that cannot
    /// establish publisher authorship: a missing or unparseable manifest, an unsigned manifest, an empty
    /// baked set, an untrusted signature, a tampered hash, or a below-floor epoch (INT007/INT008/INT009/
    /// INT001/INT002/INT010 from <see cref="PayloadIntegrityGate"/>).
    /// </summary>
    private Result<VerifiedAdvance> VerifyAndResolveAdvance(string manifestJson, int storedEpoch)
    {
        if (string.IsNullOrEmpty(manifestJson))
            return Result<VerifiedAdvance>.Failure(ErrorKind.SecurityError,
                "TrustStateAdvance request carries no signed manifest; refusing to advance the trust store " +
                "without proof of publisher authorship.");

        InstallerManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(
                manifestJson, BundleTrustJsonContext.Default.InstallerManifest);
        }
        catch (JsonException)
        {
            return Result<VerifiedAdvance>.Failure(ErrorKind.SecurityError,
                "TrustStateAdvance request carries an unparseable manifest.");
        }

        if (manifest is null)
            return Result<VerifiedAdvance>.Failure(ErrorKind.SecurityError,
                "TrustStateAdvance request carries an empty manifest.");

        // Require-signed UPDATE path: a signature is mandatory (an absent one is INT007, an empty baked set
        // INT009), the anti-downgrade epoch is enforced (INT008), and the operation resolves from the signed
        // epoch relative to the real stored epoch — a same-epoch advance is an Update (one release key), an
        // epoch advance is a KeyChange (release + recovery quorum).
        var policy = TrustPolicy.FromBakedKeys(
            _trustedFingerprints, _trustedRoles, _trustedPqCompanions,
            requireSigned: true, isUpdatePath: true, storedEpoch: storedEpoch);
        var gate = PayloadIntegrityGate.Verify(manifest, policy);
        if (gate.IsFailure)
            return Result<VerifiedAdvance>.Failure(ErrorKind.SecurityError, gate.Error.Message);

        // Take the epoch + revocations from the SIGNED envelope (the verified object), never the wire. The
        // gate guaranteed the signature is present and parseable, so this re-parse cannot fail; guard anyway.
        if (manifest.ManifestSignature is not { } signatureJson)
            return Result<VerifiedAdvance>.Failure(ErrorKind.SecurityError,
                "TrustStateAdvance request manifest lost its signature after verification.");

        var envelope = IntegrityEnvelopeCodec.Parse(signatureJson);
        if (envelope is null)
            return Result<VerifiedAdvance>.Failure(ErrorKind.SecurityError,
                "TrustStateAdvance request manifest signature could not be re-parsed.");

        return new VerifiedAdvance(envelope.Epoch, envelope.Revoked ?? []);
    }

    /// <summary>The verified epoch + revocations to persist, taken from the signature-verified envelope.</summary>
    private readonly record struct VerifiedAdvance(int Epoch, IReadOnlyList<string> Revoked);
}
