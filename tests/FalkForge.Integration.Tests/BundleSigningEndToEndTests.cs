using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Unit D: end-to-end proof that a bundle signed at build time (Compiler.Bundle) is
/// accepted by the engine-side verification contract (Engine.Protocol.Integrity), and
/// that tampering is rejected. This crosses the project boundary with a real fluent
/// build + compile, so it catches any drift between the signer's serialization and the
/// verifier's canonical byte computation that the per-project unit tests cannot.
///
/// <para>The verification here replicates exactly what the engine's PayloadIntegrityGate
/// does — verify the envelope signature, then bind each signed entry to its manifest
/// package hash — because that gate is internal to the engine assembly. The gate's own
/// wrapper (and its ApplyStep wiring) is covered by FalkForge.Engine.Tests.</para>
/// </summary>
public sealed class BundleSigningEndToEndTests
{
    /// <summary>
    /// Mirrors PayloadIntegrityGate.Verify: signature valid AND every signed entry binds
    /// to a manifest package whose hash matches. Returns null on success or a reason string.
    /// </summary>
    private static string? GateVerify(InstallerManifest manifest)
    {
        if (manifest.ManifestSignature is null)
            return null; // unsigned: backward compatible

        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
        if (envelope is null)
            return "INT003: malformed envelope";

        if (!IntegrityEnvelopeCodec.VerifySignature(envelope))
            return "INT001: signature invalid";

        foreach (var entry in envelope.Files)
        {
            var pkg = manifest.Packages.FirstOrDefault(p => p.Id == entry.Name);
            if (pkg is null)
                return $"INT002: signed entry '{entry.Name}' has no package";
            if (!string.Equals(pkg.Sha256Hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                return $"INT002: hash mismatch for '{entry.Name}'";
        }

        return null;
    }

    private static InstallerManifest ExtractManifest(string bundlePath)
    {
        var content = PayloadEmbedder.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.Value.ManifestJsonBytes!);
        Assert.NotNull(manifest);
        return manifest!;
    }

    private static (string msiPath, string dir) FakePayload(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"falk-sign-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var msiPath = Path.Combine(dir, name);
        File.WriteAllBytes(msiPath, RandomNumberGenerator.GetBytes(512));
        return (msiPath, dir);
    }

    [Fact]
    public void SignedBundle_EnginePassesVerification()
    {
        var (msiPath, dir) = FakePayload("App.msi");
        try
        {
            var model = new BundleBuilder()
                .Name("SignedE2E")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .UseSilentUI()
                .Integrity(i => { })   // ephemeral key, no sigil needed
                .Chain(chain => chain.MsiPackage(msiPath, pkg => pkg.Id("AppMsi").Version("1.0.0")))
                .Build();

            var result = new BundleCompiler().Compile(model, Path.Combine(dir, "out"));
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

            var manifest = ExtractManifest(result.Value);
            Assert.NotNull(manifest.ManifestSignature);
            Assert.Null(GateVerify(manifest)); // engine accepts the signed bundle
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TamperedPayloadHash_EngineRejectsWithSecurityFailure()
    {
        var (msiPath, dir) = FakePayload("App.msi");
        try
        {
            var model = new BundleBuilder()
                .Name("TamperE2E")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .UseSilentUI()
                .Integrity(i => { })
                .Chain(chain => chain.MsiPackage(msiPath, pkg => pkg.Id("AppMsi").Version("1.0.0")))
                .Build();

            var result = new BundleCompiler().Compile(model, Path.Combine(dir, "out"));
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

            var manifest = ExtractManifest(result.Value);

            // Simulate an attacker swapping the payload and rewriting the (unsigned)
            // package hash to match the swap. The signed envelope still says the original
            // hash, so binding fails — the engine must refuse to install.
            var tampered = MutateFirstPackageHash(manifest, "DEADBEEFDEADBEEFDEADBEEFDEADBEEFDEADBEEFDEADBEEFDEADBEEFDEADBEEF");

            var reason = GateVerify(tampered);
            Assert.NotNull(reason);
            Assert.Contains("INT002", reason);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reproducible_FixedKey_SignsIdenticalContentHashes()
    {
        // Signing must compose with Reproducible(): the SIGNED content (payload hashes)
        // is identical across two reproducible builds with the same key. Only the ECDSA
        // signature bytes — an intentionally non-deterministic, post-content addition —
        // may differ.
        var pemPath = Path.Combine(Path.GetTempPath(), $"falk-key-{Guid.NewGuid():N}.pem");
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            File.WriteAllText(pemPath, key.ExportPkcs8PrivateKeyPem());

        var (msiPath, dir) = FakePayload("App.msi");
        try
        {
            InstallerManifest BuildOnce(string outName)
            {
                var model = new BundleBuilder()
                    .Name("ReproE2E")
                    .Manufacturer("Integration Tests")
                    .Version("1.0.0")
                    .UseSilentUI()
                    .Reproducible(epochOverride: 1_700_000_000)
                    .Integrity(i => i.SigningKey(pemPath))
                    .Chain(chain => chain.MsiPackage(msiPath, pkg => pkg.Id("AppMsi").Version("1.0.0")))
                    .Build();
                var result = new BundleCompiler().Compile(model, Path.Combine(dir, outName));
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
                return ExtractManifest(result.Value);
            }

            var first = IntegrityEnvelopeCodec.Parse(BuildOnce("o1").ManifestSignature!)!;
            var second = IntegrityEnvelopeCodec.Parse(BuildOnce("o2").ManifestSignature!)!;

            // Same key, same content -> same embedded public key and same signed file hashes.
            Assert.Equal(first.Signatures[0].PublicKey, second.Signatures[0].PublicKey);
            Assert.Equal(first.Files.Count, second.Files.Count);
            for (var i = 0; i < first.Files.Count; i++)
            {
                Assert.Equal(first.Files[i].Name, second.Files[i].Name);
                Assert.Equal(first.Files[i].Sha256, second.Files[i].Sha256);
            }
            // Both envelopes verify.
            Assert.True(IntegrityEnvelopeCodec.VerifySignature(first));
            Assert.True(IntegrityEnvelopeCodec.VerifySignature(second));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            File.Delete(pemPath);
        }
    }

    private static InstallerManifest MutateFirstPackageHash(InstallerManifest manifest, string newHash)
    {
        var packages = manifest.Packages.ToArray();
        var original = packages[0];
        packages[0] = new PackageInfo
        {
            Id = original.Id,
            Type = original.Type,
            DisplayName = original.DisplayName,
            Version = original.Version,
            Vital = original.Vital,
            SourcePath = original.SourcePath,
            Sha256Hash = newHash,
            Properties = original.Properties,
            ContainerId = original.ContainerId
        };

        return new InstallerManifest
        {
            Name = manifest.Name,
            Manufacturer = manifest.Manufacturer,
            Version = manifest.Version,
            BundleId = manifest.BundleId,
            UpgradeCode = manifest.UpgradeCode,
            Scope = manifest.Scope,
            Packages = packages,
            ManifestSignature = manifest.ManifestSignature,
            SbomAttestation = manifest.SbomAttestation
        };
    }
}
