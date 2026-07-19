using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// A distributed bundle exe runs alone: the engine embedded as its PE front finds no
/// <c>FalkForge.Engine.Elevation.exe</c> beside itself, so per-machine (elevated) installs are
/// silently impossible. These tests encode the fix: a runnable bundle CARRIES the elevation
/// companion as a trust-covered payload — present in the overlay TOC, its SHA-256 declared in the
/// manifest (<see cref="InstallerManifest.EngineCompanionSha256"/>), and, when the bundle is
/// integrity-signed, covered by the ECDSA signature envelope exactly like every other payload.
/// The companion executes as SYSTEM, so it must never ride outside the payload-trust chain.
/// </summary>
/// <remarks>
/// "BundleIntegrityEnv" collection: some cases here compile with Integrity configured and
/// depend on signing actually running — see <see cref="BundleCompilerSigningTests"/> for why
/// every bundle-integrity-env-mutating class in this assembly shares one collection.
/// </remarks>
[Collection("BundleIntegrityEnv")]
public sealed class ElevationCompanionEmbeddingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _payloadPath;

    public ElevationCompanionEmbeddingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ElevCompanion_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _payloadPath = Path.Combine(_tempDir, "payload.msi");
        File.WriteAllBytes(_payloadPath, [0xD0, 0xCF, 0x11, 0xE0, 0x00]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private BundleModel BuildModel(
        string name = "CompanionBundle",
        bool omitCompanion = false,
        IntegrityConfiguration? integrity = null,
        string? packageId = null) => new()
    {
        Name = name,
        Manufacturer = "Contoso",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = InstallScope.PerMachine,
        OmitElevationCompanion = omitCompanion,
        Integrity = integrity,
        Packages = new List<BundlePackageModel>
        {
            new()
            {
                Id = packageId ?? "payload.msi",
                SourcePath = _payloadPath,
                Type = BundlePackageType.MsiPackage,
                DisplayName = "Payload"
            }
        }.AsReadOnly()
    };

    private string WriteFakeExe(string fileName, byte marker)
    {
        var path = Path.Combine(_tempDir, fileName);
        var bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        bytes[2] = marker;
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private (string Engine, string Companion) WritePublishLayout()
    {
        var engine = WriteFakeExe("fake-engine.exe", 0x01);
        var companion = WriteFakeExe(EngineCompanionPayload.PackageId, 0x02);
        return (engine, companion);
    }

    private static (InstallerManifest Manifest, TocEntry[] Entries) ReadBundle(string bundlePath)
    {
        var content = BundleReader.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        Assert.NotNull(content.Value.ManifestJsonBytes);
        var manifest = JsonSerializer.Deserialize(
            content.Value.ManifestJsonBytes!, ManifestJsonContext.Default.InstallerManifest);
        Assert.NotNull(manifest);
        return (manifest!, content.Value.TocEntries);
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // ── default: engine embedded → companion embedded, hash in the manifest ──

    [Fact]
    public void Compile_EngineEmbedded_CarriesCompanionInTocAndDeclaresHashInManifest()
    {
        var (engine, companion) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-default"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);

        var companionEntry = Assert.Single(
            entries, e => e.PackageId == EngineCompanionPayload.PackageId);
        var expectedHash = Sha256Of(companion);
        Assert.Equal(expectedHash, companionEntry.Sha256Hash, ignoreCase: true);
        Assert.Equal(expectedHash, manifest.EngineCompanionSha256, ignoreCase: true);
    }

    [Fact]
    public void Compile_EngineEmbedded_CompanionBytesExtractByteForByte()
    {
        var (engine, companion) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-bytes"));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var (_, entries) = ReadBundle(result.Value);
        var entry = Assert.Single(entries, e => e.PackageId == EngineCompanionPayload.PackageId);
        var extracted = BundleReader.ExtractPayload(result.Value, entry);
        Assert.True(extracted.IsSuccess, extracted.IsFailure ? extracted.Error.Message : null);
        Assert.Equal(File.ReadAllBytes(companion), extracted.Value);
    }

    [Fact]
    public void Compile_DefaultResolver_FindsCompanionBesideResolvedEngine()
    {
        var (engine, companion) = WritePublishLayout();
        var compiler = new BundleCompiler
        {
            EngineStubResolver = () => Result<string>.Success(engine)
        };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-resolver"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, _) = ReadBundle(result.Value);
        Assert.Equal(Sha256Of(companion), manifest.EngineCompanionSha256, ignoreCase: true);
    }

    // ── signed bundles: the companion is inside the ECDSA-signed set ─────────

    [Fact]
    public void Compile_WithIntegrity_SignatureEnvelopeCoversCompanion()
    {
        var (engine, companion) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(
            BuildModel(integrity: new IntegrityConfiguration()),
            Path.Combine(_tempDir, "out-signed"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);
        Assert.NotNull(manifest.ManifestSignature);

        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!);
        Assert.NotNull(envelope);
        var signedEntry = Assert.Single(
            envelope!.Files, f => f.Name == EngineCompanionPayload.PackageId);
        Assert.Equal(Sha256Of(companion), signedEntry.Sha256, ignoreCase: true);

        // The full byte→TOC→signed binding the engine enforces at bootstrap must hold:
        // a companion-carrying signed bundle passes SignedPayloadTocVerifier untampered.
        var verify = SignedPayloadTocVerifier.Verify(
            manifest, entries,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.True(verify.IsSuccess, verify.IsFailure ? verify.Error.Message : null);
    }

    // ── opt-outs: per-user-only authoring and the design-time placeholder ────

    [Fact]
    public void Compile_OmitElevationCompanion_CarriesNoCompanion()
    {
        var (engine, _) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(
            BuildModel(omitCompanion: true), Path.Combine(_tempDir, "out-omit"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);
        Assert.Null(manifest.EngineCompanionSha256);
        Assert.DoesNotContain(entries, e => e.PackageId == EngineCompanionPayload.PackageId);
    }

    [Fact]
    public void Compile_PlaceholderStub_CarriesNoCompanion_AndStaysHermetic()
    {
        // A design-time bundle has no engine to elevate for; ambient machine state
        // (env var, publish output) must not leak a companion into it.
        var compiler = new BundleCompiler { AllowPlaceholderStub = true };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-placeholder"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);
        Assert.Null(manifest.EngineCompanionSha256);
        Assert.DoesNotContain(entries, e => e.PackageId == EngineCompanionPayload.PackageId);
    }

    // ── fail loud: a runnable bundle missing its companion is a broken build ──

    [Fact]
    public void Compile_EngineEmbedded_CompanionUnresolvable_FailsLoudWithGuidance()
    {
        // Engine present, no companion beside it, no explicit path, no opt-out: silently
        // shipping a bundle that can never elevate would be the exact gap this feature closes.
        var engine = WriteFakeExe("fake-engine.exe", 0x01);
        var compiler = new BundleCompiler { EngineStubPath = engine };
        var outDir = Path.Combine(_tempDir, "out-missing");

        var result = compiler.Compile(BuildModel(), outDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains(EngineCompanionPayload.PackageId, result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("OmitElevationCompanion", result.Error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(outDir, "CompanionBundle.exe")));
    }

    [Fact]
    public void Compile_ExplicitCompanionPathMissing_FailsLoud()
    {
        var engine = WriteFakeExe("fake-engine.exe", 0x01);
        var compiler = new BundleCompiler
        {
            EngineStubPath = engine,
            ElevationCompanionPath = Path.Combine(_tempDir, "no-such-companion.exe")
        };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-explicit-missing"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("ElevationCompanionPath", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_ExplicitCompanionPath_WinsOverBesideEngineProbe()
    {
        var (engine, _) = WritePublishLayout();
        var explicitCompanion = WriteFakeExe("custom-companion.exe", 0x77);
        var compiler = new BundleCompiler
        {
            EngineStubPath = engine,
            ElevationCompanionPath = explicitCompanion
        };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-explicit"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, _) = ReadBundle(result.Value);
        Assert.Equal(Sha256Of(explicitCompanion), manifest.EngineCompanionSha256, ignoreCase: true);
    }

    // ── reserved payload id: a user package must never impersonate the companion ──

    [Fact]
    public void Compile_UserPackageWithReservedCompanionId_FailsLoud()
    {
        var (engine, _) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(
            BuildModel(packageId: EngineCompanionPayload.PackageId),
            Path.Combine(_tempDir, "out-collision"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("reserved", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── delta bundles carry the companion the same way ────────────────────────

    [Fact]
    public void DeltaCompile_EngineEmbedded_CarriesCompanionAndDeclaresHash()
    {
        var (engine, companion) = WritePublishLayout();

        var baseResult = new BundleCompiler { EngineStubPath = engine }
            .Compile(BuildModel("DeltaBase"), Path.Combine(_tempDir, "delta-base"));
        Assert.True(baseResult.IsSuccess, baseResult.IsFailure ? baseResult.Error.Message : null);

        var deltaCompiler = new DeltaBundleCompiler { EngineStubPath = engine };
        var result = deltaCompiler.Compile(
            BuildModel("DeltaNew"), Path.Combine(_tempDir, "delta-out"), baseResult.Value);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);
        var companionEntry = Assert.Single(
            entries, e => e.PackageId == EngineCompanionPayload.PackageId);
        var expectedHash = Sha256Of(companion);
        Assert.Equal(expectedHash, manifest.EngineCompanionSha256, ignoreCase: true);
        // Whether stored full or as a delta, the hash the extractor will verify the finished
        // companion bytes against must be the manifest-declared one.
        var boundHash = companionEntry.IsDelta
            ? companionEntry.ReconstructedSha256Hash
            : companionEntry.Sha256Hash;
        Assert.Equal(expectedHash, boundHash, ignoreCase: true);
    }
}
