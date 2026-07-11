namespace FalkForge.Engine.Tests;

using FalkForge.Engine;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// The bootstrapper's last gate before the elevation companion can ever be wired for elevated
/// execution: the extracted companion (whose bytes the extractor already verified against the
/// overlay TOC hash) must bind to the hash the manifest DECLARES
/// (<see cref="InstallerManifest.EngineCompanionSha256"/>) — for signed bundles that declaration
/// sits inside the ECDSA-verified chain. A companion the manifest never declared is NEVER wired
/// (an undeclared SYSTEM binary must not run), a declared-but-missing or hash-mismatched
/// companion fails loud, and a bundle with no companion resolves to none (per-user fallback).
/// </summary>
public sealed class BootstrapCompanionResolverTests : IDisposable
{
    private readonly string _cacheDir;

    public BootstrapCompanionResolverTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), $"CompanionResolve_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    private static InstallerManifest Manifest(string? companionSha256) => new()
    {
        Name = "App",
        Manufacturer = "Mfg",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = InstallScope.PerMachine,
        Packages = [],
        EngineCompanionSha256 = companionSha256
    };

    private static TocEntry CompanionEntry(
        string sha256, bool isDelta = false, string? reconstructedSha256 = null) => new()
    {
        PackageId = EngineCompanionPayload.PackageId,
        Offset = 0,
        CompressedSize = 10,
        OriginalSize = 10,
        Sha256Hash = sha256,
        IsDelta = isDelta,
        ReconstructedSha256Hash = reconstructedSha256
    };

    private string WriteExtractedCompanion()
    {
        var path = Path.Combine(_cacheDir, EngineCompanionPayload.PackageId);
        File.WriteAllBytes(path, [(byte)'M', (byte)'Z', 0x01]);
        return path;
    }

    [Fact]
    public void Resolve_ManifestDeclaresNoCompanion_ReturnsNone_PerUserFallback()
    {
        var result = BootstrapCompanionResolver.Resolve(
            Manifest(companionSha256: null), [], _cacheDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(result.Value.VerifiedPath);
    }

    [Fact]
    public void Resolve_UndeclaredCompanionEntryInToc_ReturnsNone_NeverWiresUndeclaredBinary()
    {
        // A TOC payload under the reserved name that the manifest never declared is an opaque
        // payload at best and a smuggled binary at worst — it must never be wired for elevation.
        WriteExtractedCompanion();

        var result = BootstrapCompanionResolver.Resolve(
            Manifest(companionSha256: null), [CompanionEntry("AABB")], _cacheDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(result.Value.VerifiedPath);
    }

    [Fact]
    public void Resolve_DeclaredAndTocHashMatches_ReturnsExtractedPath()
    {
        var extracted = WriteExtractedCompanion();

        var result = BootstrapCompanionResolver.Resolve(
            Manifest("AABB"), [CompanionEntry("AABB")], _cacheDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(extracted, result.Value.VerifiedPath);
    }

    [Fact]
    public void Resolve_HashComparisonIsCaseInsensitive()
    {
        WriteExtractedCompanion();

        var result = BootstrapCompanionResolver.Resolve(
            Manifest("aabb"), [CompanionEntry("AABB")], _cacheDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(result.Value.VerifiedPath);
    }

    [Fact]
    public void Resolve_TocHashDisagreesWithManifestDeclaration_FailsLoud_TamperSignal()
    {
        // The extractor verifies bytes against the TOC hash; the manifest declares the trusted
        // hash. Disagreement means the companion payload (or its TOC entry) was swapped after the
        // manifest was authored — the SYSTEM binary must not be wired, and the caller aborts.
        WriteExtractedCompanion();

        var result = BootstrapCompanionResolver.Resolve(
            Manifest("AABB"), [CompanionEntry("BEEF")], _cacheDir);

        Assert.True(result.IsFailure, "tampered companion must fail verification, never wire");
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains(EngineCompanionPayload.PackageId, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_DeclaredButTocCarriesNoCompanion_FailsLoud()
    {
        var result = BootstrapCompanionResolver.Resolve(
            Manifest("AABB"), [], _cacheDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void Resolve_DeclaredAndBoundButExtractedFileMissing_FailsLoud()
    {
        // No file was extracted at the expected cache path — never silently continue as if a
        // verified companion existed.
        var result = BootstrapCompanionResolver.Resolve(
            Manifest("AABB"), [CompanionEntry("AABB")], _cacheDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void Resolve_DeltaCompanion_BindsReconstructedHash()
    {
        // For a delta-carried companion the extractor verifies the RECONSTRUCTED bytes against
        // ReconstructedSha256Hash; the delta-blob hash is irrelevant to trust (same rule as
        // SignedPayloadTocVerifier).
        WriteExtractedCompanion();

        var result = BootstrapCompanionResolver.Resolve(
            Manifest("AABB"),
            [CompanionEntry("1111", isDelta: true, reconstructedSha256: "AABB")],
            _cacheDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(result.Value.VerifiedPath);
    }

    [Fact]
    public void Resolve_DeltaCompanion_ReconstructedHashMismatch_FailsLoud()
    {
        WriteExtractedCompanion();

        var result = BootstrapCompanionResolver.Resolve(
            Manifest("AABB"),
            [CompanionEntry("AABB", isDelta: true, reconstructedSha256: "BEEF")],
            _cacheDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }
}
