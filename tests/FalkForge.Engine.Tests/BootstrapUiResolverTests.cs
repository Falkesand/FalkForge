namespace FalkForge.Engine.Tests;

using FalkForge.Engine;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// The bootstrapper's gate before the UI process is launched. The extracted UI (whose bytes the
/// extractor already verified against the overlay TOC hash) must bind to the hash the manifest
/// DECLARES (<see cref="InstallerManifest.EngineUiSha256"/>); for signed bundles that declaration
/// sits inside the ECDSA-verified chain. The UI drives the engine over the session pipe, and on a
/// companion-carrying bundle the engine holds an elevated gateway, so a UI the manifest never
/// declared is never launched, and a declared-but-missing or hash-mismatched UI fails loud.
/// </summary>
public sealed class BootstrapUiResolverTests : IDisposable
{
    private readonly string _cacheDir;

    public BootstrapUiResolverTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), $"UiResolve_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose() => TestTemp.TryDelete(_cacheDir);

    private static InstallerManifest Manifest(string? uiSha256) => new()
    {
        Name = "App",
        Manufacturer = "Mfg",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = InstallScope.PerMachine,
        Packages = [],
        EngineUiSha256 = uiSha256
    };

    private static TocEntry UiEntry(
        string sha256, bool isDelta = false, string? reconstructedSha256 = null) => new()
    {
        PackageId = UiPayload.PackageId,
        Offset = 0,
        CompressedSize = 10,
        OriginalSize = 10,
        Sha256Hash = sha256,
        IsDelta = isDelta,
        ReconstructedSha256Hash = reconstructedSha256
    };

    private string WriteExtractedUi()
    {
        var path = Path.Combine(_cacheDir, UiPayload.PackageId);
        File.WriteAllBytes(path, [(byte)'M', (byte)'Z', 0x03]);
        return path;
    }

    [Fact]
    public void Resolve_ManifestDeclaresNoUi_ReturnsNone()
    {
        // Older bundles and design-time placeholder builds carry no UI. The resolver reports that
        // plainly; the caller turns it into the abort message rather than guessing a launch target.
        var result = BootstrapUiResolver.Resolve(Manifest(uiSha256: null), [], _cacheDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(result.Value.VerifiedPath);
    }

    [Fact]
    public void Resolve_UndeclaredUiEntryInToc_ReturnsNone_NeverLaunchesUndeclaredBinary()
    {
        // A TOC payload under the reserved id that the manifest never declared is a smuggled
        // binary. It must never become the process the engine hands its pipe secret to.
        WriteExtractedUi();

        var result = BootstrapUiResolver.Resolve(Manifest(uiSha256: null), [UiEntry("AABB")], _cacheDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(result.Value.VerifiedPath);
    }

    [Fact]
    public void Resolve_DeclaredAndTocHashMatches_ReturnsExtractedPath()
    {
        var extracted = WriteExtractedUi();

        var result = BootstrapUiResolver.Resolve(Manifest("AABB"), [UiEntry("AABB")], _cacheDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(extracted, result.Value.VerifiedPath);
    }

    [Theory]
    [InlineData(false, "AABB", null, "AABB")]
    // A delta payload is trusted on the hash of the file it reconstructs to, not on the hash of
    // the delta blob, so that is the value the launch site has to prove the bytes against.
    [InlineData(true, "DELTA-BLOB", "AABB", "AABB")]
    public void Resolve_CarriesTheBoundDigestForwardWithThePath(
        bool isDelta, string tocHash, string? reconstructedHash, string expectedDigest)
    {
        WriteExtractedUi();

        var result = BootstrapUiResolver.Resolve(
            Manifest("AABB"), [UiEntry(tocHash, isDelta, reconstructedHash)], _cacheDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(expectedDigest, result.Value.ExpectedSha256);
    }

    [Fact]
    public void Resolve_DeclaredButNoTocEntry_FailsLoud()
    {
        var result = BootstrapUiResolver.Resolve(Manifest("AABB"), [], _cacheDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains(UiPayload.PackageId, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_DeclaredHashDisagreesWithToc_FailsLoud()
    {
        WriteExtractedUi();

        var result = BootstrapUiResolver.Resolve(Manifest("AABB"), [UiEntry("BEEF")], _cacheDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("BEEF", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_DeclaredAndBoundButFileMissing_FailsLoud()
    {
        // Extraction did not actually produce the file. Continuing would launch a path that is
        // not there, or one an attacker creates in the gap.
        var result = BootstrapUiResolver.Resolve(Manifest("AABB"), [UiEntry("AABB")], _cacheDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }
}
