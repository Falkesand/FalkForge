namespace FalkForge.Engine.Elevation.Tests.Commands;

using System.Security.Cryptography;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Elevation.Tests.Mocks;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// The elevated companion must not install an MSI it cannot trace to a publisher key it holds itself. These
/// tests drive <see cref="MsiInstallCommand.Execute"/> with payloads that carry a signed manifest and assert
/// the require-signed gate refuses everything that cannot establish authorship for an installable MSI — the
/// mock installer is never called on any rejection. The trusted-key set is injected so the gate runs with a
/// known publisher rather than the (empty) baked set of a framework build.
/// </summary>
public sealed class MsiInstallCommandTrustTests
{
    private const string MsiPath = @"C:\does-not-exist\app.msi";
    private const string PackageId = "App.Main";

    private readonly MockMsiApi _msi = new();

    private MsiInstallCommand Command(IReadOnlySet<string> trusted) =>
        new(_msi, new NoopStaging(), trusted, SignedManifestPayload.NoRoles, SignedManifestPayload.NoPqCompanions);

    [Fact]
    public void Execute_EnvelopeLessPayload_Rejected_NeverInstalls()
    {
        // A payload with no manifest must be refused, never treated as a legacy allow-through.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = SignedManifestPayload.Build(MsiPath, string.Empty, PackageId, manifestJson: string.Empty);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("no signed manifest", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _msi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_UnsignedManifest_Rejected_NeverInstalls()
    {
        // A manifest with no signature envelope on the require-signed path is INT007.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.ManifestJson(
            envelopeEntries: [], packages: [(PackageId, new string('A', 64))], preUI: [], companionSha256: null,
            signingKey: null);
        var payload = SignedManifestPayload.Build(MsiPath, string.Empty, PackageId, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT007", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _msi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_EmptyBakedSet_Rejected_NeverInstalls()
    {
        // S1: an empty baked set on the require-signed path fails closed with INT009 — a required signature
        // with no trust anchor cannot establish authorship.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.ManifestJson(PackageId, new string('A', 64), key);
        var payload = SignedManifestPayload.Build(MsiPath, string.Empty, PackageId, manifestJson);

        var result = Command(new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT009", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _msi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_SignedByUntrustedKey_Rejected_NeverInstalls()
    {
        // S2: file+manifest self-consistent, but the signing fingerprint is not in a non-empty baked set.
        using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.ManifestJson(PackageId, new string('A', 64), publisher);
        var payload = SignedManifestPayload.Build(MsiPath, string.Empty, PackageId, manifestJson);

        // Trust only the stranger; the bundle is signed by the publisher. The baked set gives every trusted
        // key a role, so the gate runs the quorum path: no trusted signature is collected, so the Install
        // quorum is unsatisfied (INT010) — the bundle is refused for lack of an anchored publisher.
        var result = Command(SignedManifestPayload.TrustedSet(stranger)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _msi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_TamperedManifestHash_Rejected_NeverInstalls()
    {
        // S3: a correctly-signed envelope whose accompanying manifest package hash was edited to an attacker
        // MSI hash is rejected (INT002). Proves the bind is to the SIGNED hash, not the manifest's declared
        // hash.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedHash = new string('A', 64);
        var attackerHash = new string('B', 64);
        var manifestJson = SignedManifestPayload.ManifestJson(
            envelopeEntries: [(PackageId, signedHash)],
            packages: [(PackageId, attackerHash)],
            preUI: [], companionSha256: null, signingKey: key);
        var payload = SignedManifestPayload.Build(MsiPath, string.Empty, PackageId, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT002", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _msi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_NamesTheElevationCompanion_Rejected_NeverInstalls()
    {
        // S4: even a fully-signed companion entry is refused — the companion payload is not an installable
        // MSI. The manifest legitimately covers both an installable package and the companion, so the gate
        // itself passes; the installable-package guard is what rejects the request.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var companionHash = new string('C', 64);
        var manifestJson = SignedManifestPayload.ManifestJson(
            envelopeEntries: [(PackageId, new string('A', 64)), (EngineCompanionPayload.PackageId, companionHash)],
            packages: [(PackageId, new string('A', 64))],
            preUI: [], companionSha256: companionHash, signingKey: key);
        var payload = SignedManifestPayload.Build(
            MsiPath, string.Empty, EngineCompanionPayload.PackageId, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("companion", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _msi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_NamesAPreUIPrerequisite_Rejected_NeverInstalls()
    {
        // S4: a signed pre-UI prerequisite (a PE, not an MSI) named as the install target is refused.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string preReqId = "DotNetRuntime";
        var preReqHash = new string('D', 64);
        var preUI = new[]
        {
            new PreUIPackageInfo
            {
                Id = preReqId,
                DisplayName = preReqId,
                SourcePath = preReqId,
                Sha256Hash = preReqHash,
                Arguments = "/quiet"
            }
        };
        var manifestJson = SignedManifestPayload.ManifestJson(
            envelopeEntries: [(PackageId, new string('A', 64)), (preReqId, preReqHash)],
            packages: [(PackageId, new string('A', 64))],
            preUI: preUI, companionSha256: null, signingKey: key);
        var payload = SignedManifestPayload.Build(MsiPath, string.Empty, preReqId, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("pre-UI", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _msi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_DuplicatePackageId_Rejected_NeverInstalls()
    {
        // S4: a manifest carrying the named id twice (the runtime duplicate-id gap — legit hash first,
        // attacker hash second) is refused outright.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedHash = new string('A', 64);
        var manifestJson = SignedManifestPayload.ManifestJson(
            envelopeEntries: [(PackageId, signedHash)],
            packages: [(PackageId, signedHash), (PackageId, new string('E', 64))],
            preUI: [], companionSha256: null, signingKey: key);
        var payload = SignedManifestPayload.Build(MsiPath, string.Empty, PackageId, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("duplicat", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _msi.InstallProductCallCount);
    }
}
