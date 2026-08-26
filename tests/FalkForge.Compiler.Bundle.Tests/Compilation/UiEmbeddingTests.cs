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
/// A distributed bundle exe extracts itself, verifies its payloads, and then has nothing to launch:
/// it exits 1 with "No UI executable found in bundle payloads". These tests encode the fix: a
/// runnable bundle CARRIES <c>FalkForge.Ui.exe</c> as a trust-covered payload — present in the
/// overlay TOC under the reserved id, its SHA-256 declared in the manifest
/// (<see cref="InstallerManifest.EngineUiSha256"/>), and, when the bundle is integrity-signed,
/// covered by the ECDSA signature envelope exactly like every other payload.
/// <para>The UI drives the engine over the session pipe, and on a companion-carrying bundle the
/// engine holds an elevated gateway, so the UI must never ride outside the payload-trust chain.</para>
/// </summary>
/// <remarks>
/// "BundleIntegrityEnv" collection: some cases here compile with Integrity configured and depend on
/// signing actually running — see <see cref="BundleCompilerSigningTests"/> for why every
/// bundle-integrity-env-mutating class in this assembly shares one collection.
/// </remarks>
[Collection("BundleIntegrityEnv")]
public sealed class UiEmbeddingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _payloadPath;

    public UiEmbeddingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UiEmbed_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _payloadPath = Path.Combine(_tempDir, "payload.msi");
        File.WriteAllBytes(_payloadPath, [0xD0, 0xCF, 0x11, 0xE0, 0x00]);
    }

    public void Dispose() => TestTemp.TryDelete(_tempDir);

    private BundleModel BuildModel(
        string name = "UiBundle",
        IntegrityConfiguration? integrity = null,
        string? packageId = null,
        string? containerId = null) => new()
    {
        Name = name,
        Manufacturer = "Contoso",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = InstallScope.PerMachine,
        Integrity = integrity,
        Containers = containerId is null
            ? []
            : new List<ContainerModel>
            {
                new() { Id = containerId, DownloadUrl = "https://example.invalid/extra.container" }
            }.AsReadOnly(),
        Packages = new List<BundlePackageModel>
        {
            new()
            {
                Id = packageId ?? "payload.msi",
                SourcePath = _payloadPath,
                Type = BundlePackageType.MsiPackage,
                DisplayName = "Payload",
                ContainerId = containerId
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

    /// <summary>
    /// The publish layout a real build sees: engine, elevation companion and UI in one directory.
    /// Tests point the compiler at the engine, and the UI is resolved from beside it, so no machine
    /// state leaks in.
    /// </summary>
    private (string Engine, string Ui) WritePublishLayout()
    {
        var engine = WriteFakeExe("fake-engine.exe", 0x01);
        WriteFakeExe(EngineCompanionPayload.PackageId, 0x02);
        var ui = WriteFakeExe(UiPayload.PackageId, 0x03);
        return (engine, ui);
    }

    private static (InstallerManifest Manifest, TocEntry[] Entries) ReadBundle(string bundlePath)
    {
        var content = BundleReader.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        Assert.NotNull(content.Value.ManifestJsonBytes);
        var manifest = JsonSerializer.Deserialize(
            content.Value.ManifestJsonBytes, ManifestJsonContext.Default.InstallerManifest);
        Assert.NotNull(manifest);
        return (manifest, content.Value.TocEntries);
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // ── the TOC carries the reserved id and the manifest declares its hash ────

    [Fact]
    public void Compile_EngineEmbedded_CarriesUiInTocAndDeclaresHashInManifest()
    {
        var (engine, ui) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-default"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);

        var uiEntry = Assert.Single(entries, e => e.PackageId == UiPayload.PackageId);
        var expectedHash = Sha256Of(ui);
        Assert.Equal(expectedHash, uiEntry.Sha256Hash, ignoreCase: true);
        Assert.Equal(expectedHash, manifest.EngineUiSha256, ignoreCase: true);
    }

    [Fact]
    public void Compile_EngineEmbedded_UiBytesExtractByteForByte()
    {
        var (engine, ui) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-bytes"));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var (_, entries) = ReadBundle(result.Value);
        var entry = Assert.Single(entries, e => e.PackageId == UiPayload.PackageId);
        var extracted = BundleReader.ExtractPayload(result.Value, entry);
        Assert.True(extracted.IsSuccess, extracted.IsFailure ? extracted.Error.Message : null);
        Assert.Equal(File.ReadAllBytes(ui), extracted.Value);
    }

    // ── signed bundles: the UI is inside the ECDSA-signed set ────────────────

    [Fact]
    public void Compile_WithIntegrity_SignatureEnvelopeCoversUi_AndTheGateAccepts()
    {
        var (engine, ui) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(
            BuildModel(integrity: new IntegrityConfiguration()),
            Path.Combine(_tempDir, "out-signed"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);
        Assert.NotNull(manifest.ManifestSignature);

        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
        Assert.NotNull(envelope);
        var signedEntry = Assert.Single(envelope.Files, f => f.Name == UiPayload.PackageId);
        Assert.Equal(Sha256Of(ui), signedEntry.Sha256, ignoreCase: true);

        // The full byte -> TOC -> signed binding the engine enforces at bootstrap must hold on an
        // untampered UI-carrying signed bundle.
        var verify = SignedPayloadTocVerifier.Verify(
            manifest, entries, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.True(verify.IsSuccess, verify.IsFailure ? verify.Error.Message : null);
    }

    // ── design-time placeholder: no engine, so no UI ─────────────────────────

    [Fact]
    public void Compile_PlaceholderStub_CarriesNoUi_AndStaysHermetic()
    {
        // A design-time bundle has no engine to talk to a UI, and machine state (env var, publish
        // output) must not leak a multi-megabyte UI into it.
        var compiler = new BundleCompiler { AllowPlaceholderStub = true };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-placeholder"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);
        Assert.Null(manifest.EngineUiSha256);
        Assert.DoesNotContain(entries, e => e.PackageId == UiPayload.PackageId);
    }

    // ── the UI is found beside an explicitly configured engine ──────────────

    /// <summary>
    /// A build that points the compiler at a published engine, with the UI beside it in the same
    /// publish directory, must resolve the UI from there too. The elevation companion already
    /// resolved that way, so the same layout used to hand back the companion and then fail on the
    /// UI with "No published FalkForge.Ui.exe could be located".
    /// </summary>
    [Fact]
    public void Compile_ExplicitEngineStubPath_ResolvesTheUiBesideIt()
    {
        var (engine, ui) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-beside-stub"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);
        var uiEntry = Assert.Single(entries, e => e.PackageId == UiPayload.PackageId);
        var expectedHash = Sha256Of(ui);
        Assert.Equal(expectedHash, uiEntry.Sha256Hash, ignoreCase: true);
        Assert.Equal(expectedHash, manifest.EngineUiSha256, ignoreCase: true);
    }

    [Fact]
    public void DeltaCompile_ExplicitEngineStubPath_ResolvesTheUiBesideIt()
    {
        var (engine, ui) = WritePublishLayout();

        var baseResult = new BundleCompiler { EngineStubPath = engine }
            .Compile(BuildModel("BesideStubBase"), Path.Combine(_tempDir, "beside-stub-base"));
        Assert.True(baseResult.IsSuccess, baseResult.IsFailure ? baseResult.Error.Message : null);

        var deltaCompiler = new DeltaBundleCompiler { EngineStubPath = engine };
        var result = deltaCompiler.Compile(
            BuildModel("BesideStubNew"), Path.Combine(_tempDir, "beside-stub-out"), baseResult.Value);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, _) = ReadBundle(result.Value);
        Assert.Equal(Sha256Of(ui), manifest.EngineUiSha256, ignoreCase: true);
    }

    // ── fail loud: a runnable bundle whose UI cannot be resolved ─────────────

    [Fact]
    public void Compile_ExplicitUiPathMissing_FailsLoud()
    {
        var engine = WriteFakeExe("fake-engine.exe", 0x01);
        WriteFakeExe(EngineCompanionPayload.PackageId, 0x02);
        var compiler = new BundleCompiler
        {
            EngineStubPath = engine,
            UiPath = Path.Combine(_tempDir, "no-such-ui.exe")
        };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-explicit-missing"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("UiPath", result.Error.Message, StringComparison.Ordinal);
    }

    // ── reserved payload id: nothing authored may impersonate the UI ─────────

    [Fact]
    public void Compile_AuthoredPackageWithReservedUiId_FailsLoud()
    {
        var (engine, ui) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(
            BuildModel(packageId: UiPayload.PackageId),
            Path.Combine(_tempDir, "out-collision"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("reserved", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_AuthoredExeInExternalContainerUsingReservedUiId_FailsLoud()
    {
        // ExternalContainerPackager splits embedded from external payloads BEFORE the appender's
        // own reserved-id guard runs, so the guard never sees an external payload. Validation is
        // the only check that sees both halves: without it an authored payload in a downloadable
        // container extracts to {cacheDir}/FalkForge.Ui.exe at runtime, on top of the real UI.
        var (engine, ui) = WritePublishLayout();
        var compiler = new BundleCompiler { EngineStubPath = engine };

        var result = compiler.Compile(
            BuildModel(packageId: UiPayload.PackageId, containerId: "extern"),
            Path.Combine(_tempDir, "out-container-shadow"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("reserved", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── delta bundles carry the UI the same way ──────────────────────────────

    [Fact]
    public void DeltaCompile_EngineEmbedded_CarriesUiAndDeclaresHash()
    {
        var (engine, ui) = WritePublishLayout();

        var baseResult = new BundleCompiler { EngineStubPath = engine }
            .Compile(BuildModel("DeltaBase"), Path.Combine(_tempDir, "delta-base"));
        Assert.True(baseResult.IsSuccess, baseResult.IsFailure ? baseResult.Error.Message : null);

        var deltaCompiler = new DeltaBundleCompiler { EngineStubPath = engine };
        var result = deltaCompiler.Compile(
            BuildModel("DeltaNew"), Path.Combine(_tempDir, "delta-out"), baseResult.Value);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);
        var uiEntry = Assert.Single(entries, e => e.PackageId == UiPayload.PackageId);
        var expectedHash = Sha256Of(ui);
        Assert.Equal(expectedHash, manifest.EngineUiSha256, ignoreCase: true);

        // Whether stored full or as a delta, the hash the extractor will verify the finished UI
        // bytes against must be the manifest-declared one. Wiring the appender AFTER the payload
        // list is snapshotted for delta diffing would leave this entry out of the snapshot.
        var boundHash = uiEntry.IsDelta ? uiEntry.ReconstructedSha256Hash : uiEntry.Sha256Hash;
        Assert.Equal(expectedHash, boundHash, ignoreCase: true);
    }

    [Fact]
    public void DeltaCompile_WithIntegrity_SignatureEnvelopeCoversUi()
    {
        // Round 2's finding: an appender wired after the snapshot produces a bundle whose TOC has
        // the UI but whose signed set does not, which fails INT004 at install instead of at build.
        var (engine, ui) = WritePublishLayout();

        var baseResult = new BundleCompiler { EngineStubPath = engine }
            .Compile(BuildModel("DeltaSignedBase"), Path.Combine(_tempDir, "delta-signed-base"));
        Assert.True(baseResult.IsSuccess, baseResult.IsFailure ? baseResult.Error.Message : null);

        var deltaCompiler = new DeltaBundleCompiler { EngineStubPath = engine };
        var result = deltaCompiler.Compile(
            BuildModel("DeltaSignedNew", integrity: new IntegrityConfiguration()),
            Path.Combine(_tempDir, "delta-signed-out"),
            baseResult.Value);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var (manifest, entries) = ReadBundle(result.Value);
        Assert.NotNull(manifest.ManifestSignature);

        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
        Assert.NotNull(envelope);
        Assert.Contains(envelope.Files, f => f.Name == UiPayload.PackageId);

        var verify = SignedPayloadTocVerifier.Verify(
            manifest, entries, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.True(verify.IsSuccess, verify.IsFailure ? verify.Error.Message : null);
    }
}
