using System.Text.Json;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Unit B: BundleCompiler must embed a verifiable ECDSA signature when the model
/// requests integrity, using the pure-.NET path so it works with no sigil CLI on
/// PATH. The embedded envelope must verify and bind to the manifest package hashes
/// exactly as the engine gate expects.
/// </summary>
public sealed class BundleCompilerSigningTests : IDisposable
{
    private readonly string _tempDir;

    public BundleCompilerSigningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleSignTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreatePayload(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private BundleModel ModelWithIntegrity(IntegrityConfiguration? integrity, params (string id, string path)[] packages)
    {
        var pkgs = packages
            .Select(p => new BundlePackageModel
            {
                Id = p.id,
                SourcePath = p.path,
                Type = BundlePackageType.MsiPackage,
                DisplayName = p.id
            })
            .ToList();

        return new BundleModel
        {
            Name = "SignedBundle",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = pkgs.AsReadOnly(),
            Integrity = integrity
        };
    }

    private InstallerManifest ExtractManifest(string bundlePath)
    {
        var content = PayloadEmbedder.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        Assert.NotNull(content.Value.ManifestJsonBytes);
        var manifest = JsonSerializer.Deserialize(
            content.Value.ManifestJsonBytes!, ManifestJsonContext.Default.InstallerManifest);
        Assert.NotNull(manifest);
        return manifest!;
    }

    [Fact]
    public void Compile_WithoutIntegrity_LeavesManifestUnsigned()
    {
        var p = CreatePayload("a.msi", "payload-a");
        var model = ModelWithIntegrity(integrity: null, ("PkgA", p));

        var result = new BundleCompiler().Compile(model, Path.Combine(_tempDir, "out1"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(ExtractManifest(result.Value).ManifestSignature);
    }

    [Fact]
    public void Compile_WithIntegrity_EphemeralKey_EmbedsVerifiableSignature_WithoutSigil()
    {
        var p = CreatePayload("a.msi", "payload-a");
        var model = ModelWithIntegrity(new IntegrityConfiguration(), ("PkgA", p));

        var result = new BundleCompiler().Compile(model, Path.Combine(_tempDir, "out2"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var manifest = ExtractManifest(result.Value);

        Assert.NotNull(manifest.ManifestSignature);
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!);
        Assert.NotNull(envelope);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope!));

        // The signed entry binds to the package and carries its real payload hash.
        var entry = Assert.Single(envelope!.Files);
        Assert.Equal("PkgA", entry.Name);
        Assert.Equal(manifest.Packages[0].Sha256Hash, entry.Sha256);
    }

    [Fact]
    public void Compile_WithIntegrity_MultiplePackages_SignsEveryPayload()
    {
        var a = CreatePayload("a.msi", "payload-a");
        var b = CreatePayload("b.msi", "payload-b-different");
        var model = ModelWithIntegrity(new IntegrityConfiguration(), ("PkgA", a), ("PkgB", b));

        var result = new BundleCompiler().Compile(model, Path.Combine(_tempDir, "out3"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var manifest = ExtractManifest(result.Value);
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!)!;

        Assert.Equal(2, envelope.Files.Count);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
        foreach (var pkg in manifest.Packages)
        {
            var match = envelope.Files.Single(f => f.Name == pkg.Id);
            Assert.Equal(pkg.Sha256Hash, match.Sha256);
        }
    }
}
