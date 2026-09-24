using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Elevation.Tests.Mocks;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

/// <summary>
/// The companion runs msiexec as SYSTEM with whatever properties the unelevated caller sends. A
/// same-user caller who cannot forge a trusted signature must not be able to set a property the
/// publisher did not sign for, on either channel: the command-line arguments or the secret block.
/// An envelope below version 3 carries no signed allowlist and no signed version; this companion refuses
/// it, so a replayed pre-allowlist manifest cannot reach the shape-only path through it.
/// </summary>
public sealed class MsiInstallCommandPropertyAllowlistTests : IDisposable
{
    private const string PackageId = "App.Main";

    private readonly string _msiPath;
    private readonly MockMsiApi _mockMsiApi = new();
    private readonly ECDsa _publisherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly MsiInstallCommand _command;

    public MsiInstallCommandPropertyAllowlistTests()
    {
        var raw = Path.Combine(Path.GetTempPath(), $"allow_{Guid.NewGuid():N}.msi");
        File.WriteAllBytes(raw, [0x00]);
        _msiPath = ResolveFinalPath(raw);
        _command = new MsiInstallCommand(
            _mockMsiApi,
            new NoopStaging(),
            SignedManifestPayload.TrustedSet(_publisherKey),
            SignedManifestPayload.NoRoles,
            SignedManifestPayload.NoPqCompanions);
    }

    public void Dispose()
    {
        _publisherKey.Dispose();
        if (File.Exists(_msiPath))
            File.Delete(_msiPath);
    }

    private static string ResolveFinalPath(string path)
    {
        using var stream = File.OpenRead(path);
        return HashBoundFile.TryGetFinalPath(stream.SafeFileHandle) ?? path;
    }

    private string Hash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_msiPath)));

    private byte[] Payload(string args, string[]? allowed, (string name, byte[] value)[]? secrets = null)
    {
        var manifest = SignedManifestPayload.ManifestJson(PackageId, Hash(), _publisherKey, allowed);
        return SignedManifestPayload.Build(_msiPath, args, PackageId, manifest, secrets);
    }

    private void AssertRefusedNeverInstalled(Result<byte[]> result, string mustMention)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains(mustMention, result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_PropertyInSignedAllowlist_Installs()
    {
        var result = _command.Execute(Payload(" INSTALLDIR=\"C:\\App\"", ["INSTALLDIR", "LICENSEKEY"]));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_PropertyOutsideSignedAllowlist_Refused_NeverInstalls()
    {
        var result = _command.Execute(Payload(" INSTALLDIR=\"C:\\App\" DBPASSWORD=\"x\"", ["INSTALLDIR"]));

        AssertRefusedNeverInstalled(result, "DBPASSWORD");
    }

    [Fact]
    public void Execute_V3EnvelopeWithoutAllowlist_WithProperty_Refused()
    {
        // A v3 envelope with no allowlist field means the publisher declared nothing, so nothing is allowed.
        var result = _command.Execute(Payload(" INSTALLDIR=\"C:\\App\"", allowed: null));

        AssertRefusedNeverInstalled(result, "INSTALLDIR");
    }

    [Fact]
    public void Execute_V3EnvelopeWithoutAllowlist_NoProperties_Installs()
    {
        var result = _command.Execute(Payload(string.Empty, allowed: null));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_EmptySignedAllowlist_WithProperty_Refused()
    {
        var result = _command.Execute(Payload(" INSTALLDIR=\"C:\\App\"", allowed: []));

        AssertRefusedNeverInstalled(result, "INSTALLDIR");
    }

    [Fact]
    public void Execute_AllowlistSignedForOtherPackage_Refused()
    {
        var manifest = SignedManifestPayload.ManifestJson(
            envelopeEntries: [(PackageId, Hash())],
            packages: [(PackageId, Hash())],
            preUI: [],
            companionSha256: null,
            signingKey: _publisherKey,
            propertyAllowlists: [("Other.Pkg", ["INSTALLDIR"])]);
        var payload = SignedManifestPayload.Build(_msiPath, " INSTALLDIR=\"C:\\App\"", PackageId, manifest);

        var result = _command.Execute(payload);

        AssertRefusedNeverInstalled(result, "INSTALLDIR");
    }

    [Fact]
    public void Execute_TwoAllowlistEntriesForSamePackage_Refused()
    {
        // The compiler's own BDL005 check never lets two packages share an id, so this shape only
        // reaches the companion via a rewritten manifest. It must be refused, not resolved by picking
        // either entry.
        var manifest = SignedManifestPayload.ManifestJson(
            envelopeEntries: [(PackageId, Hash())],
            packages: [(PackageId, Hash())],
            preUI: [],
            companionSha256: null,
            signingKey: _publisherKey,
            propertyAllowlists: [(PackageId, ["INSTALLDIR"]), (PackageId, ["LICENSEKEY"])]);
        var payload = SignedManifestPayload.Build(_msiPath, " INSTALLDIR=\"C:\\App\"", PackageId, manifest);

        var result = _command.Execute(payload);

        AssertRefusedNeverInstalled(result, "more than one property allowlist");
    }

    [Fact]
    public void Execute_QuotedValueContainingSpace_Installs()
    {
        // The value is quoted, so the space inside it is not a second property. Only INSTALLDIR is
        // allowlisted; a parser that mistook "B=y" for a second key would refuse this on B.
        var result = _command.Execute(Payload(" INSTALLDIR=\"x B=y\"", ["INSTALLDIR"]));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_EmptyQuotedValue_Installs()
    {
        var result = _command.Execute(Payload(" INSTALLDIR=\"\"", ["INSTALLDIR"]));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_SeveralSpacesBetweenAllowedPairs_Installs()
    {
        var result = _command.Execute(Payload(" INSTALLDIR=\"C:\\App\"   LICENSEKEY=\"abc\"", ["INSTALLDIR", "LICENSEKEY"]));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_AllowlistNameDiffersOnlyByCase_Refused()
    {
        // The companion's name rule is upper-case only, and the match is ordinal. A lower-case entry in
        // the signed list cannot be reached by any argument the shape check lets through, so it grants
        // nothing rather than silently matching.
        var result = _command.Execute(Payload(" INSTALLDIR=\"C:\\App\"", ["installdir"]));

        AssertRefusedNeverInstalled(result, "INSTALLDIR");
    }

    [Fact]
    public void Execute_TransformsInSignedAllowlist_StillRefused()
    {
        // Even a publisher cannot allowlist TRANSFORMS: the shape check runs first and refuses it.
        var result = _command.Execute(Payload(" TRANSFORMS=\"evil.mst\"", ["TRANSFORMS"]));

        AssertRefusedNeverInstalled(result, "TRANSFORMS");
    }

    [Fact]
    public void Execute_SecretPropertyOutsideSignedAllowlist_Refused_BeforeStaging()
    {
        // The secret channel is the second way a caller sets a property. NoopStaging throws if the
        // companion reaches transform generation, so a refusal here proves the allowlist ran first.
        var payload = Payload(string.Empty, ["INSTALLDIR"], secrets: [("DBPASSWORD", "p@ss"u8.ToArray())]);

        var result = _command.Execute(payload);

        AssertRefusedNeverInstalled(result, "DBPASSWORD");
    }

    [Fact]
    public void Execute_TamperedAllowlist_Refused_NeverInstalls()
    {
        var manifest = SignedManifestPayload.TamperedPropertyAllowlistManifestJson(
            PackageId, Hash(), _publisherKey, signedNames: ["INSTALLDIR"], tamperedNames: ["INSTALLDIR", "DBPASSWORD"]);
        var payload = SignedManifestPayload.Build(_msiPath, " DBPASSWORD=\"x\"", PackageId, manifest);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        // The trust gate resolves every baked fingerprint to a role (defaulting to Release) and always
        // evaluates the signing quorum for the install operation, so a signature that no longer covers the
        // rewritten bytes is reported as an unsatisfied quorum (INT010), not the roles-off match failure
        // (INT001) that only applies when no roles are configured at all.
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_EnvelopeBelowVersion3_Refused_NeverInstalls()
    {
        // A version 2 envelope carries no allowlist and no signed version. A caller who replays one the
        // publisher really signed must not get the shape-only property path through this companion. The
        // bundle it came from still installs through its own embedded companion; this one never sees a
        // version 2 envelope from a compiler of its own release.
        var files = new List<ManifestFileEntry> { new() { Name = PackageId, Sha256 = Hash() } };
        var hash = SHA256.HashData(IntegrityEnvelopeCodec.ComputeSignedBytes(files));
        var spki = _publisherKey.ExportSubjectPublicKeyInfo();
        var v2 = new ManifestSignatureEnvelope
        {
            Version = 2,
            Algorithm = IntegrityEnvelopeCodec.AlgorithmId,
            Files = files,
            Epoch = 0,
            Revoked = [],
            Signatures =
            [
                new SignatureEntry
                {
                    KeyId = string.Empty,
                    Fingerprint = SignedManifestPayload.Fingerprint(_publisherKey),
                    PublicKey = Convert.ToBase64String(spki),
                    Signature = Convert.ToBase64String(FalkForge.Signing.EcdsaLowS.Canonicalize(_publisherKey.SignHash(hash)))
                }
            ]
        };
        var manifest = SignedManifestPayload.ManifestJsonWithEnvelope(PackageId, Hash(), IntegrityEnvelopeCodec.Serialize(v2));
        var payload = SignedManifestPayload.Build(_msiPath, " INSTALLDIR=\"C:\\App\"", PackageId, manifest);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Contains("version 2", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    // A same-user caller can rewrite the manifest freely. A null inside the allowlist must come back
    // as a refusal from the parser, never as an exception out of the SYSTEM process and never as an
    // install.
    [Theory]
    [InlineData("\"propertyAllowlists\":[null]")]
    [InlineData("\"propertyAllowlists\":[{\"packageId\":\"App.Main\",\"propertyNames\":null}]")]
    public void Execute_NullInsideSignedAllowlist_Refused_NeverInstalls(string allowlistJson)
    {
        var files = new List<ManifestFileEntry> { new() { Name = PackageId, Sha256 = Hash() } };
        var envelope = IntegrityEnvelopeCodec.Sign(
            files, [_publisherKey], epoch: 0, revoked: [], externalContainers: null,
            transformAssociations: null, productCodes: null,
            propertyAllowlists: [new PackagePropertyAllowlist { PackageId = PackageId, PropertyNames = ["INSTALLDIR"] }]);
        var json = IntegrityEnvelopeCodec.Serialize(envelope);
        var tampered = json.Replace(
            "\"propertyAllowlists\":[{\"packageId\":\"App.Main\",\"propertyNames\":[\"INSTALLDIR\"]}]",
            allowlistJson, StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);
        var manifest = SignedManifestPayload.ManifestJsonWithEnvelope(PackageId, Hash(), tampered);
        var payload = SignedManifestPayload.Build(_msiPath, " INSTALLDIR=\"C:\\App\"", PackageId, manifest);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }
}
