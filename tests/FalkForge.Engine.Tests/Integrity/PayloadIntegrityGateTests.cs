namespace FalkForge.Engine.Tests.Integrity;

using System.Security.Cryptography;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// The engine-side gate that runs before any payload executes. Stage 1 (C14) makes it prove
/// <b>authorship</b>, not just internal consistency: a signature is accepted only when its key's
/// fingerprint is in the engine's pinned trusted set. That closes the re-sign bypass (B1) — an
/// attacker who rewrites and re-signs a bundle with their own key is rejected. The gate still binds
/// each signed entry to its manifest package hash and requires full set coverage. An unpinned engine
/// (empty set) falls back to consistency-only; an unsigned manifest passes unless a signature is required.
/// </summary>
public sealed class PayloadIntegrityGateTests
{
    private static InstallerManifest ManifestWith(string? signature, params PackageInfo[] packages)
        => new()
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = packages,
            ManifestSignature = signature
        };

    private static PackageInfo Package(string id, string sha256)
        => new()
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = id,
            SourcePath = $"C:/cache/{id}.msi",
            Sha256Hash = sha256
        };

    private static string SignEnvelope(ECDsa key, params (string id, string hash)[] entries)
    {
        var files = entries
            .Select(e => new ManifestFileEntry { Name = e.id, Sha256 = e.hash })
            .ToList();
        return IntegrityEnvelopeCodec.Serialize(IntegrityEnvelopeCodec.Sign(files, key));
    }

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    private static TrustPolicy Pinned(ECDsa key, bool requireSigned = false)
        => new(new HashSet<string>(new[] { Fingerprint(key) }, StringComparer.OrdinalIgnoreCase), requireSigned);

    [Fact]
    public void Verify_NoSignature_ConsistencyOnly_ReturnsSuccess_BackwardCompatible()
    {
        var manifest = ManifestWith(signature: null, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, TrustPolicy.ConsistencyOnly);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Verify_NoSignature_RequireSigned_ReturnsInt007()
    {
        var manifest = ManifestWith(signature: null, Package("A", "AABB"));

        var policy = new TrustPolicy(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), requireSigned: true);
        var result = PayloadIntegrityGate.Verify(manifest, policy);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT007", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_ValidSignature_TrustedKey_HashesMatch_ReturnsSuccess()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "AABB"), ("B", "CCDD"));
        var manifest = ManifestWith(sig, Package("A", "AABB"), Package("B", "CCDD"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(key));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Verify_ValidSignature_EmptyTrustedSet_ConsistencyOnly_ReturnsSuccess()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, TrustPolicy.ConsistencyOnly);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Verify_TamperedPackageHash_NotInSignedSet_ReturnsInt002()
    {
        // The signed envelope says A=AABB, but the manifest package now claims A=BEEF
        // (as if an attacker swapped the payload and rewrote the unsigned package hash).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "BEEF"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(key));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT002", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_ForgedSignature_WrongKey_ReturnsInt001()
    {
        // Build an envelope signed by one key but advertise a different public key in its entry.
        using var realKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = new List<ManifestFileEntry> { new() { Name = "A", Sha256 = "AABB" } };
        var envelope = IntegrityEnvelopeCodec.Sign(files, realKey);
        // Swap the entry's public key so the declared fingerprint no longer matches its key.
        envelope.Signatures[0].PublicKey = Convert.ToBase64String(wrongKey.ExportSubjectPublicKeyInfo());
        var manifest = ManifestWith(IntegrityEnvelopeCodec.Serialize(envelope), Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(realKey));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_MalformedEnvelope_ReturnsInt003()
    {
        var manifest = ManifestWith("{ not valid json }}}", Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, TrustPolicy.ConsistencyOnly);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT003", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_SignedEntryHasNoMatchingPackage_ReturnsInt002()
    {
        // Envelope signs a payload "Ghost" that the manifest does not contain — a signed
        // entry with no package to bind it to is a contract violation, not an install.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("Ghost", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(key));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("Ghost", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_SignedManifest_PackageNotInSignedSet_ReturnsInt004()
    {
        // The envelope signs only package A. The manifest carries A (signed) AND an extra package B
        // that is NOT in the signed set — an attacker appending an unsigned package to a signed bundle.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "AABB"), Package("B", "CCDD"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(key));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT004", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_ReSigningAttacker_UntrustedKey_ReturnsInt001_ClosesB1()
    {
        // B1 at the engine gate: the attacker fully re-signs a rewritten bundle with THEIR OWN key and
        // embeds their own public key — a self-consistent envelope the old gate accepted. With the
        // publisher's key pinned, the attacker's fingerprint is not trusted, so the gate rejects it.
        using var publisherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var attackerSig = SignEnvelope(attackerKey, ("A", "AABB"));
        var manifest = ManifestWith(attackerSig, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(publisherKey));

        Assert.True(result.IsFailure, "A bundle re-signed with an untrusted key must be rejected (B1).");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_TrustedPublisher_MatchingKey_ReturnsSuccess()
    {
        // The genuine publisher signs; the engine pins that same key. Pin matches, every package is
        // covered, hashes bind -> success. This is what proves authorship.
        using var publisherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(publisherKey, ("A", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(publisherKey));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Verify_SignedManifest_RequireSigned_EmptyTrustedSet_ReturnsInt009_FailClosed()
    {
        // Defense-in-depth mirror of SignedPayloadTocVerifier's INT009 guard: on a require-signed path
        // with no baked publisher key, a present signature cannot establish authorship (an empty set
        // means consistency-only accept-any). Fail closed rather than fall open, so a future caller that
        // flips RequireSigned on a pinless engine can never silently accept an attacker's re-signed
        // bundle. ApplyStep never sets RequireSigned today, so this changes no live behavior — it removes
        // a latent fail-open.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "AABB"));

        var policy = new TrustPolicy(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), requireSigned: true);
        var result = PayloadIntegrityGate.Verify(manifest, policy);

        Assert.True(result.IsFailure, "require-signed with an empty trusted set must fail closed");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT009", result.Error.Message, StringComparison.Ordinal);
    }

    private static InstallerManifest ManifestWithCoveredExtras(
        string? signature,
        string? companionSha256 = null,
        PreUIPackageInfo[]? preUIPackages = null,
        params PackageInfo[] packages)
        => new()
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = packages,
            ManifestSignature = signature,
            EngineCompanionSha256 = companionSha256,
            PreUIPackages = preUIPackages ?? []
        };

    private static PreUIPackageInfo PreUI(string id, string sha256)
        => new()
        {
            Id = id,
            DisplayName = id,
            SourcePath = id,
            Sha256Hash = sha256,
            Arguments = "/quiet"
        };

    [Fact]
    public void Verify_SignedCompanionEntry_BindsToManifestDeclaredHash_ReturnsSuccess()
    {
        // The elevation companion is a signed payload but not an installable package: its hash
        // lives in InstallerManifest.EngineCompanionSha256, not in Packages. The gate must bind
        // the signed companion entry to that declared hash instead of failing INT002 — the
        // companion executes as SYSTEM, so it must stay INSIDE the signed set, never outside it.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key,
            ("A", "AABB"),
            (FalkForge.Engine.Protocol.Bundle.EngineCompanionPayload.PackageId, "C0DE"));
        var manifest = ManifestWithCoveredExtras(
            sig, companionSha256: "C0DE", packages: Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(key));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Verify_SignedCompanionEntry_ManifestDeclaresDifferentHash_ReturnsInt002()
    {
        // Post-signing tamper of the manifest's companion declaration must be rejected: the
        // signed hash is the source of truth for the SYSTEM-executing companion's bytes.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key,
            ("A", "AABB"),
            (FalkForge.Engine.Protocol.Bundle.EngineCompanionPayload.PackageId, "C0DE"));
        var manifest = ManifestWithCoveredExtras(
            sig, companionSha256: "BEEF", packages: Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(key));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT002", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_SignedCompanionEntry_ManifestDeclaresNoCompanion_ReturnsInt002()
    {
        // An envelope entry for the companion with no manifest declaration to bind to is a
        // contract violation (e.g. the declaration was stripped after signing) — fail closed.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key,
            ("A", "AABB"),
            (FalkForge.Engine.Protocol.Bundle.EngineCompanionPayload.PackageId, "C0DE"));
        var manifest = ManifestWithCoveredExtras(
            sig, companionSha256: null, packages: Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(key));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT002", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_SignedPreUIEntry_BindsToPreUIPackageHash_ReturnsSuccess()
    {
        // Embedded pre-UI prerequisites are signed payloads carried in PreUIPackages, not in
        // Packages. The build-time signer covers them, so the gate must bind their signed
        // entries there — a signed Integrity() bundle with a pre-UI prerequisite previously
        // failed INT002 at apply time because the gate only consulted Packages.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "AABB"), ("DotNetRuntime", "D07E"));
        var manifest = ManifestWithCoveredExtras(
            sig,
            preUIPackages: [PreUI("DotNetRuntime", "D07E")],
            packages: Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(key));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Verify_SignedPreUIEntry_HashMismatch_ReturnsInt002()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "AABB"), ("DotNetRuntime", "D07E"));
        var manifest = ManifestWithCoveredExtras(
            sig,
            preUIPackages: [PreUI("DotNetRuntime", "BEEF")],
            packages: Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(key));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT002", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_HashComparisonIsCaseInsensitive()
    {
        // PackageCache emits uppercase hex; tolerate case so a lowercase-signed entry still binds.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "aabb"));
        var manifest = ManifestWith(sig, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, Pinned(key));

        Assert.True(result.IsSuccess);
    }
}
