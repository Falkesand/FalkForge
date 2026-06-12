namespace FalkForge.Engine.Tests.Integrity;

using System.Security.Cryptography;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// Unit C: the engine-side gate that runs before any payload executes.
/// It proves the manifest's embedded ECDSA signature covers the per-package
/// SHA-256 hashes the cache already enforces against payload bytes. A tampered
/// payload (whose bytes no longer match the signed hash) or a tampered/forged
/// manifest hash must abort the install with a SecurityError — never silently proceed.
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

    [Fact]
    public void Verify_NoSignature_ReturnsSuccess_BackwardCompatible()
    {
        var manifest = ManifestWith(signature: null, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Verify_ValidSignature_HashesMatchPackages_ReturnsSuccess()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "AABB"), ("B", "CCDD"));
        var manifest = ManifestWith(sig, Package("A", "AABB"), Package("B", "CCDD"));

        var result = PayloadIntegrityGate.Verify(manifest);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Verify_TamperedPackageHash_NotInSignedSet_ReturnsSecurityError()
    {
        // The signed envelope says A=AABB, but the manifest package now claims A=BEEF
        // (as if an attacker swapped the payload and rewrote the unsigned package hash).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "BEEF"));

        var result = PayloadIntegrityGate.Verify(manifest);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("A", result.Error.Message);
    }

    [Fact]
    public void Verify_ForgedSignature_WrongKey_ReturnsSecurityError()
    {
        // Build an envelope signed by one key but advertise a different public key.
        using var realKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = new List<ManifestFileEntry> { new() { Name = "A", Sha256 = "AABB" } };
        var envelope = IntegrityEnvelopeCodec.Sign(files, realKey);
        envelope.PublicKey = Convert.ToBase64String(wrongKey.ExportSubjectPublicKeyInfo());
        var manifest = ManifestWith(IntegrityEnvelopeCodec.Serialize(envelope), Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message);
    }

    [Fact]
    public void Verify_MalformedEnvelope_ReturnsSecurityError()
    {
        var manifest = ManifestWith("{ not valid json }}}", Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT003", result.Error.Message);
    }

    [Fact]
    public void Verify_SignedEntryHasNoMatchingPackage_ReturnsSecurityError()
    {
        // Envelope signs a payload "Ghost" that the manifest does not contain — a signed
        // entry with no package to bind it to is a contract violation, not an install.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("Ghost", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("Ghost", result.Error.Message);
    }

    [Fact]
    public void Verify_SignedManifest_PackageNotInSignedSet_ReturnsSecurityError()
    {
        // The envelope signs only package A. The manifest carries A (signed) AND an extra
        // package B that is NOT in the signed set. Without a set-coverage check, B would be
        // executed despite never having been signed — an attacker could append an unsigned
        // malicious package to a signed bundle and have it run. Every executable package in a
        // signed manifest must be covered by the signature.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "AABB"), Package("B", "CCDD"));

        var result = PayloadIntegrityGate.Verify(manifest);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("B", result.Error.Message);
    }

    [Fact]
    public void Verify_ReSigningAttacker_NoPin_PassesAndDocumentsLimitation()
    {
        // SECURITY BOUNDARY (default ephemeral / self-describing-key mode):
        // The verifying key is carried INSIDE the envelope. An attacker who fully rewrites the
        // bundle can recompute the hashes, re-sign the file list with THEIR OWN key, and embed
        // THEIR OWN public key. With no out-of-band pin, the gate has nothing to compare that key
        // against, so verification PASSES. This test pins that reality so it cannot regress
        // silently: default mode proves internal consistency / casual-tamper detection, NOT
        // authorship. Authorship requires the publisher-key pin (asserted in the next test).
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var attackerSig = SignEnvelope(attackerKey, ("A", "AABB"));
        var manifest = ManifestWith(attackerSig, Package("A", "AABB"));

        // No pin supplied -> attacker's self-consistent re-signed bundle is accepted.
        var result = PayloadIntegrityGate.Verify(manifest, expectedPublisherKeyFingerprint: null);

        Assert.True(result.IsSuccess,
            "Default mode cannot detect a full re-sign with the attacker's own key — this is the documented limitation.");
    }

    [Fact]
    public void Verify_ReSigningAttacker_WithPublisherPin_ReturnsSecurityError()
    {
        // Same attacker re-sign, but now the host pins the EXPECTED publisher key fingerprint
        // out-of-band. The attacker signed with a different key, so its embedded public key does
        // not match the pin -> the gate rejects the bundle. This is what proves authorship.
        using var publisherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var expectedFingerprint =
            Convert.ToHexString(SHA256.HashData(publisherKey.ExportSubjectPublicKeyInfo()));

        var attackerSig = SignEnvelope(attackerKey, ("A", "AABB"));
        var manifest = ManifestWith(attackerSig, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, expectedFingerprint);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT005", result.Error.Message);
    }

    [Fact]
    public void Verify_WithPublisherPin_MatchingKey_ReturnsSuccess()
    {
        // The genuine publisher signs; the host pins that same key's fingerprint. Pin matches,
        // every package is covered, hashes bind -> success.
        using var publisherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var expectedFingerprint =
            Convert.ToHexString(SHA256.HashData(publisherKey.ExportSubjectPublicKeyInfo()));

        var sig = SignEnvelope(publisherKey, ("A", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, expectedFingerprint);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Verify_WithPublisherPin_TolueratesColonSeparatedLowercaseFingerprint()
    {
        // Hosts commonly paste fingerprints in colon-separated lowercase display format.
        // The pin comparison normalizes separators and case so such a pin still matches.
        using var publisherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var raw = Convert.ToHexString(SHA256.HashData(publisherKey.ExportSubjectPublicKeyInfo()));
        var displayFormat = string.Join(':',
            Enumerable.Range(0, raw.Length / 2).Select(i => raw.Substring(i * 2, 2).ToLowerInvariant()));

        var sig = SignEnvelope(publisherKey, ("A", "AABB"));
        var manifest = ManifestWith(sig, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest, displayFormat);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Verify_HashComparisonIsCaseInsensitive()
    {
        // PackageCache emits uppercase hex; tolerate case so a lowercase-signed entry still binds.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sig = SignEnvelope(key, ("A", "aabb"));
        var manifest = ManifestWith(sig, Package("A", "AABB"));

        var result = PayloadIntegrityGate.Verify(manifest);

        Assert.True(result.IsSuccess);
    }
}
