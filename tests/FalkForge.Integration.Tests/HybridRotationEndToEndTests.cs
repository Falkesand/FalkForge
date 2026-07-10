using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Signing;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// End-to-end proof that HYBRID (ECDSA-P256 + ML-DSA-65) key rotation composes correctly — the
/// PQ-hybrid Stage 2 counterpart of <see cref="SignServerRotationRevocationE2ETests"/>'s classical
/// rotation story. Rotation is "trust leads, signing follows": during the overlap window a bundle is
/// dual-signed with the OLD and the NEW hybrid pair (each pair = one classical identity + its pinned
/// ML-DSA companion), and the verifying engine may trust both pairs, only the new pair (a migrated
/// client), or only the old pair (a not-yet-migrated client). After the cutover the old classical
/// fingerprint is revoked (or dropped from trust) and only the new pair verifies.
///
/// <para>Every scenario runs against REAL compiled bundles (<see cref="BundleCompiler"/> with the
/// fluent <c>Integrity(i =&gt; i.HybridKey(...).HybridKey(...))</c> rotation authoring) verified at
/// the engine's real trust layer (<see cref="BundleTrustVerifier.VerifyBundleContent"/> — the same
/// call the extract/bootstrap paths make), with the hybrid expectation pinned via
/// <see cref="PqCompanionPolicy"/> exactly as <c>EngineTrustAnchor.CreatePqPolicy</c> builds it.</para>
///
/// <para>Key invariants proven here, beyond the single-pair tests in
/// <c>PqHybridCompanionTests</c>: (1) verify-any accepts a dual-hybrid bundle under every realistic
/// rotation trust set, with EACH classical entry required to satisfy ITS OWN companion; (2) revoking
/// or un-trusting the old classical fingerprint retires the whole old hybrid identity while the new
/// pair keeps verifying; (3) stripping the NEW pair's ML-DSA entry never opens a downgrade window —
/// a migrated client rejects with INT011, and on the epoch-advance (KeyChange) quorum path the
/// stripped signer simply stops counting so the release+recovery quorum fails with INT010; (4) the
/// anti-downgrade epoch (INT008) is enforced unchanged for hybrid envelopes.</para>
/// </summary>
public sealed class HybridRotationEndToEndTests : IDisposable
{
    private readonly string _tempDir;

    public HybridRotationEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"HybridRotationE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>One hybrid signing identity: a classical ECDSA-P256 key + its ML-DSA-65 companion.</summary>
    private sealed record HybridPair(
        string ClassicalPemPath,
        string PqPemPath,
        string ClassicalFingerprint,
        string PqFingerprint);

    private HybridPair NewHybridPair(string name)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var mldsa = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);

        var classicalPath = Path.Combine(_tempDir, $"{name}-classical.pem");
        var pqPath = Path.Combine(_tempDir, $"{name}-mldsa.pem");
        File.WriteAllText(classicalPath, ecdsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(pqPath, mldsa.ExportPkcs8PrivateKeyPem());

        return new HybridPair(
            classicalPath,
            pqPath,
            Convert.ToHexString(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo())),
            Convert.ToHexString(SHA256.HashData(mldsa.ExportSubjectPublicKeyInfo())));
    }

    private BundleContent CompileBundle(string name, Action<FalkForge.Builders.IntegrityBuilder> integrity)
    {
        var payloadPath = Path.Combine(_tempDir, $"{name}.msi");
        File.WriteAllBytes(payloadPath, RandomNumberGenerator.GetBytes(512));

        var model = new BundleBuilder()
            .Name(name)
            .Manufacturer("Integration Tests")
            .Version("1.0.0")
            .UseSilentUI()
            .Integrity(integrity)
            .Chain(chain => chain.MsiPackage(payloadPath, pkg => pkg.Id("AppMsi").Version("1.0.0")))
            .Build();

        var buildResult = new BundleCompiler { AllowPlaceholderStub = true }.Compile(model, Path.Combine(_tempDir, $"{name}-out"));
        Assert.True(buildResult.IsSuccess, buildResult.IsFailure ? buildResult.Error.Message : null);

        var content = PayloadEmbedder.Extract(buildResult.Value);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        return content.Value;
    }

    private static ManifestSignatureEnvelope ParseEnvelope(BundleContent content)
    {
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.ManifestJsonBytes!);
        Assert.NotNull(manifest);
        Assert.NotNull(manifest!.ManifestSignature);
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!);
        Assert.NotNull(envelope);
        return envelope!;
    }

    /// <summary>
    /// Rewrites the bundle content's embedded envelope (the attacker's viewpoint: envelope entries are
    /// NOT covered by the signatures themselves, only the file list + epoch + revocations are), so a
    /// strip attack can be exercised against the real verify layer.
    /// </summary>
    private static BundleContent WithModifiedEnvelope(
        BundleContent content, Func<ManifestSignatureEnvelope, ManifestSignatureEnvelope> mutate)
    {
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.ManifestJsonBytes!)!;
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!)!;
        var mutated = manifest with { ManifestSignature = IntegrityEnvelopeCodec.Serialize(mutate(envelope)) };
        return new BundleContent
        {
            TocEntries = content.TocEntries,
            BundlePath = content.BundlePath,
            ManifestJsonBytes = JsonSerializer.SerializeToUtf8Bytes(mutated)
        };
    }

    private static ManifestSignatureEnvelope StripPqEntries(
        ManifestSignatureEnvelope envelope, params string[] pqFingerprintsToStrip)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in envelope.Signatures)
            keep.Add(entry.Fingerprint);
        foreach (var strip in pqFingerprintsToStrip)
            keep.Remove(strip);

        envelope.Signatures = envelope.Signatures.Where(s => keep.Contains(s.Fingerprint)).ToList();
        return envelope;
    }

    private static IReadOnlySet<string> Set(params string[] fingerprints) =>
        new HashSet<string>(fingerprints, StringComparer.OrdinalIgnoreCase);

    private static PqCompanionPolicy Companions(params HybridPair[] pairs) => new()
    {
        Companions = pairs.ToDictionary(
            p => p.ClassicalFingerprint, p => p.PqFingerprint, StringComparer.OrdinalIgnoreCase)
    };

    // ── (1) The rotation overlap window ───────────────────────────────────────

    [Fact]
    public void DualSignedHybridBundle_RotationOverlap_VerifiesUnderEveryRealisticTrustSet()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var oldPair = NewHybridPair("old");
        var newPair = NewHybridPair("new");

        // The rotation-window release: dual-signed with the old AND the new hybrid pair.
        var content = CompileBundle("RotationOverlap", i => i
            .HybridKey(oldPair.ClassicalPemPath, oldPair.PqPemPath)
            .HybridKey(newPair.ClassicalPemPath, newPair.PqPemPath));

        // Wire shape: two classical entries first (declaration order), then the two ML-DSA companions.
        var envelope = ParseEnvelope(content);
        Assert.Equal(4, envelope.Signatures.Count);
        Assert.Equal(oldPair.ClassicalFingerprint, envelope.Signatures[0].Fingerprint);
        Assert.Null(envelope.Signatures[0].Algorithm);
        Assert.Equal(newPair.ClassicalFingerprint, envelope.Signatures[1].Fingerprint);
        Assert.Null(envelope.Signatures[1].Algorithm);
        Assert.Equal(oldPair.PqFingerprint, envelope.Signatures[2].Fingerprint);
        Assert.Equal(IntegrityEnvelopeCodec.MlDsa65AlgorithmId, envelope.Signatures[2].Algorithm);
        Assert.Equal(newPair.PqFingerprint, envelope.Signatures[3].Fingerprint);
        Assert.Equal(IntegrityEnvelopeCodec.MlDsa65AlgorithmId, envelope.Signatures[3].Algorithm);

        // (a) Engine trusts BOTH hybrid pairs (mid-rotation) — accepts.
        var both = BundleTrustVerifier.VerifyBundleContent(
            content, Set(oldPair.ClassicalFingerprint, newPair.ClassicalFingerprint),
            pqPolicy: Companions(oldPair, newPair));
        Assert.True(both.IsSuccess, both.IsFailure ? both.Error.Message : null);

        // (b) Engine trusts ONLY the NEW pair (a client that has already migrated its trust) — accepts
        // via the new pair's classical entry + ITS OWN pinned companion.
        var onlyNew = BundleTrustVerifier.VerifyBundleContent(
            content, Set(newPair.ClassicalFingerprint), pqPolicy: Companions(newPair));
        Assert.True(onlyNew.IsSuccess, onlyNew.IsFailure ? onlyNew.Error.Message : null);

        // (c) Engine trusts ONLY the OLD pair (a client that has not migrated yet) — accepts. This is
        // the rotation overlap window working for hybrid identities.
        var onlyOld = BundleTrustVerifier.VerifyBundleContent(
            content, Set(oldPair.ClassicalFingerprint), pqPolicy: Companions(oldPair));
        Assert.True(onlyOld.IsSuccess, onlyOld.IsFailure ? onlyOld.Error.Message : null);

        // Negative control: trusting neither pair rejects with INT001 — the successes above are
        // because the trust set matters, not because verification is unconditional.
        var neither = BundleTrustVerifier.VerifyBundleContent(
            content, Set(new string('0', 64)), pqPolicy: Companions(oldPair, newPair));
        Assert.True(neither.IsFailure);
        Assert.Contains("INT001", neither.Error.Message, StringComparison.Ordinal);
    }

    // ── (2) After the cutover: revocation retires the whole old hybrid identity ──

    [Fact]
    public void AfterRotation_OldClassicalRevoked_NewPairStillVerifies_OldOnlyBundleRejected()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var oldPair = NewHybridPair("old");
        var newPair = NewHybridPair("new");

        var dualSigned = CompileBundle("RevokedOldDual", i => i
            .HybridKey(oldPair.ClassicalPemPath, oldPair.PqPemPath)
            .HybridKey(newPair.ClassicalPemPath, newPair.PqPemPath));

        // The old CLASSICAL fingerprint is revoked (the companion map is keyed by it, so revoking the
        // classical identity retires the whole hybrid pair). The dual-signed rotation bundle still
        // verifies: the revoked old entry is skipped and iteration reaches the still-good new pair.
        var dualAfterRevoke = BundleTrustVerifier.VerifyBundleContent(
            dualSigned, Set(oldPair.ClassicalFingerprint, newPair.ClassicalFingerprint),
            revokedFingerprints: Set(oldPair.ClassicalFingerprint),
            pqPolicy: Companions(oldPair, newPair));
        Assert.True(dualAfterRevoke.IsSuccess, dualAfterRevoke.IsFailure ? dualAfterRevoke.Error.Message : null);

        // A bundle signed ONLY by the retired old pair is rejected under the same revocation — a
        // revoked hybrid identity cannot sign anything anymore, PQ companion or not.
        var oldOnly = CompileBundle("RevokedOldOnly", i => i
            .HybridKey(oldPair.ClassicalPemPath, oldPair.PqPemPath));
        var oldOnlyAfterRevoke = BundleTrustVerifier.VerifyBundleContent(
            oldOnly, Set(oldPair.ClassicalFingerprint, newPair.ClassicalFingerprint),
            revokedFingerprints: Set(oldPair.ClassicalFingerprint),
            pqPolicy: Companions(oldPair, newPair));
        Assert.True(oldOnlyAfterRevoke.IsFailure,
            "a bundle signed only by the revoked old hybrid pair must be rejected");
        Assert.Contains("INT001", oldOnlyAfterRevoke.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterRotation_OldPairRemovedFromTrust_OnlyNewPairVerifies()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var oldPair = NewHybridPair("old");
        var newPair = NewHybridPair("new");

        // Post-rotation trust set: the old pair is simply gone (trust removal instead of revocation).
        var dualSigned = CompileBundle("TrustDroppedDual", i => i
            .HybridKey(oldPair.ClassicalPemPath, oldPair.PqPemPath)
            .HybridKey(newPair.ClassicalPemPath, newPair.PqPemPath));
        var dual = BundleTrustVerifier.VerifyBundleContent(
            dualSigned, Set(newPair.ClassicalFingerprint), pqPolicy: Companions(newPair));
        Assert.True(dual.IsSuccess, dual.IsFailure ? dual.Error.Message : null);

        // An old-pair-only bundle no longer verifies once the old identity is out of the trust set.
        var oldOnly = CompileBundle("TrustDroppedOldOnly", i => i
            .HybridKey(oldPair.ClassicalPemPath, oldPair.PqPemPath));
        var rejected = BundleTrustVerifier.VerifyBundleContent(
            oldOnly, Set(newPair.ClassicalFingerprint), pqPolicy: Companions(newPair));
        Assert.True(rejected.IsFailure);
        Assert.Contains("INT001", rejected.Error.Message, StringComparison.Ordinal);
    }

    // ── (3) Anti-strip: rotation never opens a downgrade window ──────────────

    [Fact]
    public void RotationBundle_StrippedNewPqCompanion_MigratedClientRejectsInt011()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var oldPair = NewHybridPair("old");
        var newPair = NewHybridPair("new");

        var content = CompileBundle("StripNewPq", i => i
            .HybridKey(oldPair.ClassicalPemPath, oldPair.PqPemPath)
            .HybridKey(newPair.ClassicalPemPath, newPair.PqPemPath));

        // Attacker strips the NEW pair's ML-DSA entry from the rotation bundle.
        var stripped = WithModifiedEnvelope(content, e => StripPqEntries(e, newPair.PqFingerprint));

        // A migrated client (trusts only the new hybrid pair) must reject: the new classical entry
        // verifies but its pinned companion is gone — INT011, not a silent classical downgrade.
        var migrated = BundleTrustVerifier.VerifyBundleContent(
            stripped, Set(newPair.ClassicalFingerprint), pqPolicy: Companions(newPair));
        Assert.True(migrated.IsFailure,
            "stripping the new pair's PQ companion must not let the bundle verify classically on a migrated client");
        Assert.Contains("INT011", migrated.Error.Message, StringComparison.Ordinal);

        // A mid-rotation client trusting BOTH pairs still accepts — via the INTACT old pair, whose own
        // companion is present. That is verify-any working as designed (the old pair is still fully
        // trusted during the window), not a downgrade: no classical-only acceptance happened.
        var window = BundleTrustVerifier.VerifyBundleContent(
            stripped, Set(oldPair.ClassicalFingerprint, newPair.ClassicalFingerprint),
            pqPolicy: Companions(oldPair, newPair));
        Assert.True(window.IsSuccess, window.IsFailure ? window.Error.Message : null);

        // Stripping BOTH pairs' ML-DSA entries leaves no satisfiable hybrid identity at all — INT011
        // even for the mid-rotation client. Rotation adds signatures, never a strip-shaped hole.
        var strippedBoth = WithModifiedEnvelope(
            content, e => StripPqEntries(e, oldPair.PqFingerprint, newPair.PqFingerprint));
        var rejectedBoth = BundleTrustVerifier.VerifyBundleContent(
            strippedBoth, Set(oldPair.ClassicalFingerprint, newPair.ClassicalFingerprint),
            pqPolicy: Companions(oldPair, newPair));
        Assert.True(rejectedBoth.IsFailure);
        Assert.Contains("INT011", rejectedBoth.Error.Message, StringComparison.Ordinal);
    }

    // ── (4) Epoch + quorum: a hybrid KeyChange composes with C19 ──────────────

    [Fact]
    public void HybridKeyChange_EpochAdvance_SatisfiesQuorum_OnlyWhenEverySignerKeepsItsPqCompanion()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var release = NewHybridPair("release");
        var recovery = NewHybridPair("recovery");

        // The rotation-cutover release: epoch bumped above the stored epoch (a KeyChange operation
        // under the baked policy), dual-signed by TWO DISTINCT hybrid identities — the release key
        // and the recovery key — as the release+recovery quorum requires.
        var content = CompileBundle("HybridKeyChange", i => i
            .HybridKey(release.ClassicalPemPath, release.PqPemPath)
            .HybridKey(recovery.ClassicalPemPath, recovery.PqPemPath)
            .Epoch(7));

        var trusted = Set(release.ClassicalFingerprint, recovery.ClassicalFingerprint);
        var roles = new Dictionary<string, TrustRole>(StringComparer.OrdinalIgnoreCase)
        {
            [release.ClassicalFingerprint] = TrustRole.Release,
            [recovery.ClassicalFingerprint] = TrustRole.Recovery
        };

        // Fully hybrid KeyChange: both classical signatures verify AND each satisfies its own pinned
        // ML-DSA companion → the quorum is met and the epoch advance is accepted.
        var accepted = BundleTrustVerifier.VerifyBundleContent(
            content, trusted, requireSigned: true, storedEpoch: 2,
            policyTable: BakedTrustPolicy.Default, roles: roles,
            pqPolicy: Companions(release, recovery));
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Message : null);

        // Strip the RECOVERY signer's ML-DSA companion: its classical signature stops counting toward
        // the quorum entirely (a hybrid-pinned signer with no valid companion contributes nothing), so
        // the release+recovery KeyChange quorum fails — INT010, no hybrid bypass of C19.
        var strippedRecovery = WithModifiedEnvelope(content, e => StripPqEntries(e, recovery.PqFingerprint));
        var quorumFailRecovery = BundleTrustVerifier.VerifyBundleContent(
            strippedRecovery, trusted, requireSigned: true, storedEpoch: 2,
            policyTable: BakedTrustPolicy.Default, roles: roles,
            pqPolicy: Companions(release, recovery));
        Assert.True(quorumFailRecovery.IsFailure,
            "a KeyChange quorum member whose PQ companion was stripped must not count toward the quorum");
        Assert.Contains("INT010", quorumFailRecovery.Error.Message, StringComparison.Ordinal);

        // Symmetric: stripping the RELEASE signer's companion also breaks the quorum.
        var strippedRelease = WithModifiedEnvelope(content, e => StripPqEntries(e, release.PqFingerprint));
        var quorumFailRelease = BundleTrustVerifier.VerifyBundleContent(
            strippedRelease, trusted, requireSigned: true, storedEpoch: 2,
            policyTable: BakedTrustPolicy.Default, roles: roles,
            pqPolicy: Companions(release, recovery));
        Assert.True(quorumFailRelease.IsFailure);
        Assert.Contains("INT010", quorumFailRelease.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HybridRotationBundle_EpochBelowStored_RejectedInt008_Unchanged()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");
        var pair = NewHybridPair("only");

        // Anti-replay stays enforced for hybrid envelopes: a superseded (lower-epoch) release is
        // rejected before any signature dispute — a rotation cutover cannot be replayed away.
        var content = CompileBundle("HybridLowEpoch", i => i
            .HybridKey(pair.ClassicalPemPath, pair.PqPemPath)
            .Epoch(3));

        var result = BundleTrustVerifier.VerifyBundleContent(
            content, Set(pair.ClassicalFingerprint), requireSigned: true, storedEpoch: 5,
            pqPolicy: Companions(pair));
        Assert.True(result.IsFailure);
        Assert.Contains("INT008", result.Error.Message, StringComparison.Ordinal);
    }
}
