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
/// A6 Stage 1 — external/downloadable containers at COMPILE time. A payload assigned to a container
/// that carries a <c>DownloadUrl</c> must be written to its own standalone container file next to the
/// bundle exe (NOT embedded in the exe), and the manifest must record the URL, the container's
/// whole-file SHA-256, the produced file name, and the container's membership. Embedded payloads (no
/// container, or a container without a URL) stay embedded. When the bundle is signed, the external
/// payload must remain inside the ECDSA-signed set so the engine can bind the downloaded container's
/// payload to the trusted publisher before extraction.
/// </summary>
/// <remarks>
/// Shares the "BundleIntegrityEnv" collection because one case compiles with Integrity configured,
/// which mutates the process-global FALKFORGE_NO_SIGN env var other bundle-signing tests depend on.
/// </remarks>
[Collection("BundleIntegrityEnv")]
public sealed class ExternalContainerCompileTests : IDisposable
{
    private const string BundleName = "ExtContainerBundle";
    private const string ContainerId = "cdn";
    private const string DownloadUrl = "https://cdn.example.com/ExtContainerBundle.cdn.ffcontainer";

    private readonly string _tempDir;
    private readonly string _embeddedPayload;
    private readonly string _externalPayload;

    public ExternalContainerCompileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ExtContainer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _embeddedPayload = Path.Combine(_tempDir, "embedded.msi");
        _externalPayload = Path.Combine(_tempDir, "external.msi");
        File.WriteAllBytes(_embeddedPayload, [0xD0, 0xCF, 0x11, 0xE0, 0x01, 0x02, 0x03]);
        File.WriteAllBytes(_externalPayload, [0xD0, 0xCF, 0x11, 0xE0, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private BundleModel BuildModel(IntegrityConfiguration? integrity = null, string? containerDownloadUrl = DownloadUrl) =>
        new()
        {
            Name = BundleName,
            Manufacturer = "Contoso",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new List<BundlePackageModel>
            {
                new()
                {
                    Id = "EmbeddedApp",
                    SourcePath = _embeddedPayload,
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "Embedded App"
                },
                new()
                {
                    Id = "ExternalApp",
                    SourcePath = _externalPayload,
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "External App",
                    ContainerId = ContainerId
                }
            }.AsReadOnly(),
            Containers = new List<ContainerModel>
            {
                new() { Id = ContainerId, DownloadUrl = containerDownloadUrl }
            }.AsReadOnly(),
            Integrity = integrity
        };

    private static InstallerManifest ExtractManifest(string bundlePath)
    {
        var content = PayloadEmbedder.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        Assert.NotNull(content.Value.ManifestJsonBytes);
        var manifest = JsonSerializer.Deserialize(
            content.Value.ManifestJsonBytes!, ManifestJsonContext.Default.InstallerManifest);
        Assert.NotNull(manifest);
        return manifest!;
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    [Fact]
    public void Compile_ExternalContainerPayload_IsNotEmbeddedInExe()
    {
        var outDir = Path.Combine(_tempDir, "out-notembed");
        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(BuildModel(), outDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var exeContent = PayloadEmbedder.Extract(result.Value);
        Assert.True(exeContent.IsSuccess, exeContent.IsFailure ? exeContent.Error.Message : null);
        var tocIds = exeContent.Value.TocEntries.Select(e => e.PackageId).ToHashSet(StringComparer.Ordinal);

        // The embedded payload stays in the exe; the external one is banished to its container file.
        Assert.Contains("EmbeddedApp", tocIds);
        Assert.DoesNotContain("ExternalApp", tocIds);
    }

    [Fact]
    public void Compile_ExternalContainer_ProducesSeparateContainerFile_WithMemberPayload()
    {
        var outDir = Path.Combine(_tempDir, "out-file");
        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(BuildModel(), outDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var containerPath = Path.Combine(outDir, $"{BundleName}.{ContainerId}.ffcontainer");
        Assert.True(File.Exists(containerPath), $"Expected external container file at {containerPath}");

        // The container file is the same FALKBUNDLE format and yields the external payload with the
        // correct per-payload hash — so the engine's normal extract+verify path handles it unchanged.
        var containerContent = PayloadEmbedder.Extract(containerPath);
        Assert.True(containerContent.IsSuccess, containerContent.IsFailure ? containerContent.Error.Message : null);
        var entry = Assert.Single(containerContent.Value.TocEntries);
        Assert.Equal("ExternalApp", entry.PackageId);
        Assert.Equal(Sha256Of(_externalPayload), entry.Sha256Hash);
    }

    [Fact]
    public void Compile_ExternalContainer_ManifestRecordsUrlHashAndMembership()
    {
        var outDir = Path.Combine(_tempDir, "out-manifest");
        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(BuildModel(), outDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var manifest = ExtractManifest(result.Value);
        var info = Assert.Single(manifest.ExternalContainers);

        Assert.Equal(ContainerId, info.Id);
        Assert.Equal(DownloadUrl, info.DownloadUrl);
        Assert.Equal($"{BundleName}.{ContainerId}.ffcontainer", info.FileName);
        Assert.Equal("ExternalApp", Assert.Single(info.PackageIds));

        // The recorded hash is the WHOLE container file's SHA-256 — the value the engine verifies the
        // downloaded bytes against before it ever opens the container.
        var containerPath = Path.Combine(outDir, info.FileName);
        Assert.Equal(Sha256Of(containerPath), info.Sha256);
    }

    [Fact]
    public void Compile_NoExternalContainers_LeavesManifestEmptyAndEmbedsEverything()
    {
        // A container with NO DownloadUrl is a pure grouping hint: its payload stays embedded.
        var outDir = Path.Combine(_tempDir, "out-local");
        var result = new BundleCompiler { AllowPlaceholderStub = true }
            .Compile(BuildModel(containerDownloadUrl: null), outDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        Assert.Empty(ExtractManifest(result.Value).ExternalContainers);
        Assert.False(File.Exists(Path.Combine(outDir, $"{BundleName}.{ContainerId}.ffcontainer")));

        var tocIds = PayloadEmbedder.Extract(result.Value).Value.TocEntries
            .Select(e => e.PackageId).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("EmbeddedApp", tocIds);
        Assert.Contains("ExternalApp", tocIds);
    }

    [Fact]
    public void Compile_SignedBundle_ExternalPayloadStaysInSignedSet()
    {
        // Integrity: the external-container payload must remain covered by the ECDSA signature even
        // though it is not embedded in the exe — that is what lets the engine bind the downloaded
        // container's bytes back to the trusted publisher (SignedPayloadTocVerifier) before extraction.
        var outDir = Path.Combine(_tempDir, "out-signed");
        var result = new BundleCompiler { AllowPlaceholderStub = true }
            .Compile(BuildModel(integrity: new IntegrityConfiguration()), outDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var manifest = ExtractManifest(result.Value);
        Assert.NotNull(manifest.ManifestSignature);
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!);
        Assert.NotNull(envelope);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope!));

        var signedNames = envelope!.Files.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("EmbeddedApp", signedNames);
        Assert.Contains("ExternalApp", signedNames);
    }
}
