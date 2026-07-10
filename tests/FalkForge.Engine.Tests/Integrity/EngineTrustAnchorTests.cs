namespace FalkForge.Engine.Tests.Integrity;

using System.Security.Cryptography;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// The code-path trust-anchor registration API (C18). It lets a publisher who rebuilds the engine add
/// trusted publisher-key fingerprints from their own compiled bootstrap code, in addition to the
/// MSBuild-baked <see cref="BakedTrustedKeys"/> set. The two sets are UNIONed and frozen once, before
/// any bundle is verified.
///
/// <para>Security invariants proven here:</para>
/// <list type="number">
///   <item><description><b>Freeze-once.</b> The effective set is established once (baked ∪ code) and
///   frozen on first read; a registration attempt after the freeze throws (fail loud) so trust can never
///   be widened after a verification has begun.</description></item>
///   <item><description><b>Same fingerprint format.</b> A code-registered public key derives the exact
///   fingerprint the verifier matches against (<see cref="IntegrityEnvelopeCodec.ComputeFingerprint"/>),
///   so registration composes with the baked set and with signed envelopes.</description></item>
///   <item><description><b>C14 semantics preserved.</b> With no registration the effective set equals the
///   baked set, so an unconfigured engine keeps its consistency-only / fail-closed behavior. A key that is
///   neither baked nor code-registered is still rejected on the require-signed path.</description></item>
/// </list>
///
/// <para>The engine test assembly disables xUnit parallelization, so the process-global anchor is reset
/// before and after every test here for deterministic isolation.</para>
/// </summary>
public sealed class EngineTrustAnchorTests : IDisposable
{
    public EngineTrustAnchorTests() => EngineTrustAnchor.ResetForTests();

    public void Dispose() => EngineTrustAnchor.ResetForTests();

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    // Reads (and thereby freezes) the effective set as a plain sequence so xUnit's Assert.Contains resolves
    // unambiguously — a FrozenSet implements both ISet and IReadOnlySet, which the Contains overloads clash on.
    private static IEnumerable<string> Effective() => EngineTrustAnchor.EffectiveFingerprints;

    private static InstallerManifest SignedManifest(ECDsa key, params (string id, string hash)[] payloads)
    {
        var files = payloads.Select(p => new ManifestFileEntry { Name = p.id, Sha256 = p.hash }).ToList();
        var envelope = IntegrityEnvelopeCodec.Sign(files, key);
        var packages = payloads.Select(p => new PackageInfo
        {
            Id = p.id,
            Type = PackageType.MsiPackage,
            DisplayName = p.id,
            SourcePath = p.id + ".msi",
            Sha256Hash = p.hash
        }).ToArray();

        return new InstallerManifest
        {
            Name = "T",
            Manufacturer = "M",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = packages,
            ManifestSignature = IntegrityEnvelopeCodec.Serialize(envelope)
        };
    }

    private static TocEntry FullEntry(string id, string tocHash) => new()
    {
        PackageId = id,
        Offset = 0,
        CompressedSize = 1,
        OriginalSize = 1,
        Sha256Hash = tocHash
    };

    // ── Effective set / union ────────────────────────────────────────────────

    [Fact]
    public void EffectiveFingerprints_NoRegistration_EqualsBakedSet_DefaultEmpty()
    {
        // The default build bakes no publisher key, so with no code registration the effective set is
        // the (empty) baked set — invariant 3: an unconfigured engine is unchanged by C18.
        Assert.Equal(BakedTrustedKeys.Fingerprints, EngineTrustAnchor.EffectiveFingerprints);
        Assert.Empty(EngineTrustAnchor.EffectiveFingerprints);
    }

    [Fact]
    public void TrustFingerprint_BeforeFreeze_IsInEffectiveSet()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fp = Fingerprint(key);

        EngineTrustAnchor.TrustFingerprint(fp);

        Assert.Contains(fp, Effective());
    }

    [Fact]
    public void TrustFingerprint_NormalizesSeparatorsAndCase()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var canonical = Fingerprint(key); // uppercase, no separators

        // A developer might paste a colon-separated, lowercase fingerprint. It must normalize to the
        // canonical form the verifier compares against.
        var display = string.Join(':', Enumerable.Range(0, canonical.Length / 2)
            .Select(i => canonical.Substring(i * 2, 2))).ToLowerInvariant();

        EngineTrustAnchor.TrustFingerprint(display);

        Assert.Contains(canonical, Effective());
    }

    [Fact]
    public void TrustPublicKey_DerivesFingerprintMatchingCodec()
    {
        // Parity: the fingerprint a code-registered SPKI derives must equal the one the envelope verifier
        // computes for the same key, or a code-registered key could never match a real signature.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = key.ExportSubjectPublicKeyInfo();

        EngineTrustAnchor.TrustPublicKey(spki);

        Assert.Contains(IntegrityEnvelopeCodec.ComputeFingerprint(spki), Effective());
    }

    [Fact]
    public void TrustPublicKeyPem_DerivesSameFingerprint()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = key.ExportSubjectPublicKeyInfo();
        var pem = key.ExportSubjectPublicKeyInfoPem();

        EngineTrustAnchor.TrustPublicKeyPem(pem);

        Assert.Contains(IntegrityEnvelopeCodec.ComputeFingerprint(spki), Effective());
    }

    // ── Freeze-once invariant ─────────────────────────────────────────────────

    [Fact]
    public void TrustFingerprint_AfterFreeze_Throws()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _ = EngineTrustAnchor.EffectiveFingerprints; // first read freezes the set

        Assert.True(EngineTrustAnchor.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => EngineTrustAnchor.TrustFingerprint(Fingerprint(key)));
    }

    [Fact]
    public void TrustPublicKey_AfterFreeze_Throws()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _ = EngineTrustAnchor.EffectiveFingerprints;

        Assert.Throws<InvalidOperationException>(() => EngineTrustAnchor.TrustPublicKey(key.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public void EffectiveFingerprints_IsStableAcrossReads_LateRegistrationRejected()
    {
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EngineTrustAnchor.TrustFingerprint(Fingerprint(k1));

        var first = EngineTrustAnchor.EffectiveFingerprints; // freeze
        Assert.Throws<InvalidOperationException>(() => EngineTrustAnchor.TrustFingerprint(Fingerprint(k2)));
        var second = EngineTrustAnchor.EffectiveFingerprints;

        Assert.Same(first, second);
        Assert.Contains(Fingerprint(k1), (IEnumerable<string>)second);
        Assert.DoesNotContain(Fingerprint(k2), (IEnumerable<string>)second);
    }

    // ── Input validation ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TrustFingerprint_NullOrWhitespace_Throws(string? value)
        => Assert.ThrowsAny<ArgumentException>(() => EngineTrustAnchor.TrustFingerprint(value!));

    [Fact]
    public void TrustFingerprint_NonHexOrWrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => EngineTrustAnchor.TrustFingerprint("not-a-fingerprint"));
        Assert.Throws<ArgumentException>(() => EngineTrustAnchor.TrustFingerprint("ABCD")); // too short
    }

    [Fact]
    public void TrustPublicKey_EmptySpki_Throws()
        => Assert.Throws<ArgumentException>(() => EngineTrustAnchor.TrustPublicKey(ReadOnlySpan<byte>.Empty));

    // ── End-to-end through the real verifier ──────────────────────────────────

    [Fact]
    public void CodeRegisteredKey_RequireSigned_VerifiesThroughRealVerifier()
    {
        // The behavioral heart of C18: a bundle signed by a key that is NOT baked but IS registered via
        // code verifies on the require-signed path. Before the API existed, an empty baked set on the
        // require-signed path fails closed with INT009 — code registration is the only way to trust it.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var manifest = SignedManifest(key, ("A", hash));

        EngineTrustAnchor.TrustPublicKey(key.ExportSubjectPublicKeyInfo());

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [FullEntry("A", hash)], EngineTrustAnchor.EffectiveFingerprints, requireSigned: true);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void NoRegistration_RequireSigned_FailsClosed_Int009()
    {
        // Invariant 3 unchanged: no baked key + no code registration on the require-signed path fails
        // closed — a signature with no trust anchor cannot establish authorship.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var manifest = SignedManifest(key, ("A", hash));

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [FullEntry("A", hash)], EngineTrustAnchor.EffectiveFingerprints, requireSigned: true);

        Assert.True(result.IsFailure);
        Assert.Contains("INT009", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyNeitherBakedNorRegistered_RequireSigned_Rejected_Int001()
    {
        // Register keyB but sign with keyA. The effective set is non-empty (authorship ON), and keyA is
        // NOT in it — the attacker's re-signed bundle is rejected. Registration does not weaken trust.
        using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var manifest = SignedManifest(attackerKey, ("A", hash));

        EngineTrustAnchor.TrustPublicKey(trustedKey.ExportSubjectPublicKeyInfo());

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [FullEntry("A", hash)], EngineTrustAnchor.EffectiveFingerprints, requireSigned: true);

        Assert.True(result.IsFailure);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    // ── Roles (C19 §3.2) ──────────────────────────────────────────────────────

    [Fact]
    public void TrustFingerprint_NoRoleArgument_DefaultsToRelease_BackwardCompat()
    {
        // §7.1: an un-roled trusted key defaults to Release so the ship-with-nothing behavior is exactly
        // C14 (install/update need one release signature, which any trusted key is).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fp = Fingerprint(key);

        EngineTrustAnchor.TrustFingerprint(fp);

        Assert.Equal(TrustRole.Release, EngineTrustAnchor.EffectiveRoles[fp]);
    }

    [Fact]
    public void TrustFingerprint_ExplicitRoles_LandInEffectiveRoles()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fp = Fingerprint(key);

        EngineTrustAnchor.TrustFingerprint(fp, TrustRole.Recovery | TrustRole.Security);

        Assert.Equal(TrustRole.Recovery | TrustRole.Security, EngineTrustAnchor.EffectiveRoles[fp]);
    }

    [Fact]
    public void TrustPublicKey_WithRoles_ResolvesRolesByDerivedFingerprint()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = key.ExportSubjectPublicKeyInfo();

        EngineTrustAnchor.TrustPublicKey(spki, TrustRole.Recovery);

        Assert.Equal(TrustRole.Recovery, EngineTrustAnchor.EffectiveRoles[IntegrityEnvelopeCodec.ComputeFingerprint(spki)]);
    }

    [Fact]
    public void DuplicateRegistration_UnionsRoles_Additive()
    {
        // The same key registered twice (or by both channels) unions its roles — additive, never a
        // replacement, matching the anchor's contract. A key that ends up release|recovery must NOT then
        // single-handedly satisfy a two-distinct-key requirement; that is the quorum evaluator's job.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fp = Fingerprint(key);

        EngineTrustAnchor.TrustFingerprint(fp, TrustRole.Release);
        EngineTrustAnchor.TrustFingerprint(fp, TrustRole.Recovery);

        Assert.Equal(TrustRole.Release | TrustRole.Recovery, EngineTrustAnchor.EffectiveRoles[fp]);
        // Still exactly one distinct trusted fingerprint.
        Assert.Single(EngineTrustAnchor.EffectiveFingerprints);
    }

    [Fact]
    public void EffectiveRoles_NoRegistration_IsEmptyButNonNull()
    {
        Assert.NotNull(EngineTrustAnchor.EffectiveRoles);
        Assert.Empty(EngineTrustAnchor.EffectiveRoles);
    }

    [Fact]
    public void EffectiveRoles_ReadFreezesTheAnchor_LateRegistrationRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EngineTrustAnchor.TrustFingerprint(Fingerprint(key), TrustRole.Release);

        _ = EngineTrustAnchor.EffectiveRoles; // freeze via the roles read

        Assert.True(EngineTrustAnchor.IsFrozen);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<InvalidOperationException>(() => EngineTrustAnchor.TrustFingerprint(Fingerprint(other)));
    }

    [Fact]
    public void CodeRegistered_NonEmptySet_FreshInstall_RejectsUntrustedSignedBundle()
    {
        // Code registration turns authorship ON exactly like a baked key: even on the fresh-install
        // (non-require-signed) path, once the effective set is non-empty an untrusted-key-signed bundle is
        // rejected (INT001), not accepted consistency-only.
        using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var manifest = SignedManifest(attackerKey, ("A", hash));

        EngineTrustAnchor.TrustPublicKey(trustedKey.ExportSubjectPublicKeyInfo());

        var result = SignedPayloadTocVerifier.Verify(
            manifest, [FullEntry("A", hash)], EngineTrustAnchor.EffectiveFingerprints, requireSigned: false);

        Assert.True(result.IsFailure);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }
}
