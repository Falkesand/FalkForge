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
