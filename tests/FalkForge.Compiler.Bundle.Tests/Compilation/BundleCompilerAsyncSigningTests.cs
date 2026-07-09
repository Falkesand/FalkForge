using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using FalkForge.Signing;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Pins the C17 stage-B async build path. A genuinely asynchronous <see cref="ISignatureProvider"/> (the
/// shape of a remote SignServer backend doing network I/O) must:
///  * fail loud (SGN010) through the synchronous <see cref="BundleCompiler.Compile"/> — the sync bridge
///    refuses to block a thread on network I/O; and
///  * sign successfully through <see cref="BundleCompiler.CompileAsync"/>, producing an embedded envelope
///    that verifies through the real <see cref="IntegrityEnvelopeCodec"/>.
/// This proves both the Prepare/Finish compile-path refactor and the async signer threading end to end.
/// </summary>
public sealed class BundleCompilerAsyncSigningTests : IDisposable
{
    private readonly string _tempDir;

    public BundleCompilerAsyncSigningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleAsyncSignTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>A provider that only completes after a real await — never synchronously — like a network call.</summary>
    private sealed class AsyncProvider(ECDsa key, string keyId) : ISignatureProvider
    {
        public async ValueTask<Result<ProviderSignature>> SignAsync(
            ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(5, cancellationToken); // forces genuine asynchrony — the ValueTask is NOT completed
            var hash = SHA256.HashData(message.Span);
            return Result<ProviderSignature>.Success(new ProviderSignature
            {
                SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo(),
                Signature = key.SignHash(hash),
                KeyId = keyId
            });
        }
    }

    private BundleModel ModelWithAsyncProvider(ECDsa key)
    {
        var payloadPath = Path.Combine(_tempDir, "a.msi");
        File.WriteAllText(payloadPath, "payload-a");

        return new BundleModel
        {
            Name = "AsyncSignedBundle",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new List<BundlePackageModel>
            {
                new()
                {
                    Id = "PkgA",
                    SourcePath = payloadPath,
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "PkgA"
                }
            }.AsReadOnly(),
            Integrity = new IntegrityConfiguration
            {
                SignatureProviders = new ISignatureProvider[] { new AsyncProvider(key, "remote-signserver") }
            }
        };
    }

    private InstallerManifest ExtractManifest(string bundlePath)
    {
        var content = PayloadEmbedder.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        var manifest = JsonSerializer.Deserialize(
            content.Value.ManifestJsonBytes!, ManifestJsonContext.Default.InstallerManifest);
        Assert.NotNull(manifest);
        return manifest!;
    }

    [Fact]
    public void Compile_SyncPath_WithAsyncProvider_FailsLoudSgn010()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var model = ModelWithAsyncProvider(key);

        var result = new BundleCompiler().Compile(model, Path.Combine(_tempDir, "sync"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN010", result.Error.Message);
    }

    [Fact]
    public async Task CompileAsync_WithAsyncProvider_EmbedsVerifiableSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var model = ModelWithAsyncProvider(key);

        var result = await new BundleCompiler().CompileAsync(model, Path.Combine(_tempDir, "async"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var manifest = ExtractManifest(result.Value);

        Assert.NotNull(manifest.ManifestSignature);
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!)!;
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));

        var entry = Assert.Single(envelope.Signatures);
        Assert.Equal("remote-signserver", entry.KeyId);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())),
            entry.Fingerprint);

        // The signed entry still binds to the real payload hash — the async path embeds identically to sync.
        var file = Assert.Single(envelope.Files);
        Assert.Equal("PkgA", file.Name);
        Assert.Equal(manifest.Packages[0].Sha256Hash, file.Sha256);
    }

    [Fact]
    public async Task CompileAsync_WithoutIntegrity_LeavesManifestUnsigned()
    {
        // The async path must preserve the unsigned behavior when no integrity is configured.
        var payloadPath = Path.Combine(_tempDir, "b.msi");
        File.WriteAllText(payloadPath, "payload-b");
        var model = new BundleModel
        {
            Name = "UnsignedAsyncBundle",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new List<BundlePackageModel>
            {
                new() { Id = "PkgB", SourcePath = payloadPath, Type = BundlePackageType.MsiPackage, DisplayName = "PkgB" }
            }.AsReadOnly(),
            Integrity = null
        };

        var result = await new BundleCompiler().CompileAsync(model, Path.Combine(_tempDir, "async-unsigned"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(ExtractManifest(result.Value).ManifestSignature);
    }
}
