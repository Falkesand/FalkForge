using System.Security.Cryptography;
using FalkForge;
using FalkForge.Signing;
using FalkForge.TestSupport;
using Xunit;

namespace FalkForge.Core.Tests.Signing;

/// <summary>
/// A bundle built with no configured signing key was signed by a throwaway key created inside
/// each signing call (<see cref="EphemeralSignatureProvider"/>), so its public half changed every
/// build and could never be pinned into the engine. These tests pin the replacement: one key,
/// written once, reused untouched afterwards, with the exact fingerprint computation the engine's
/// trust anchor uses.
/// </summary>
public sealed class PublisherKeyStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fk-keystore-").FullName;

    public void Dispose() => TestTemp.TryDelete(_dir);

    [Fact]
    public void EnsureKey_NoFile_GeneratesAP256KeyAndWritesIt()
    {
        var path = Path.Combine(_dir, "falkforge-signing.pem");

        var result = PublisherKeyStore.EnsureKey(path, allowGeneration: true);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(path));
        Assert.True(result.Value.WasGenerated);
        // The written PEM must import, and must be the curve the verifier expects.
        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(path));
        Assert.Equal(256, key.KeySize);
    }

    [Fact]
    public void EnsureKey_ExistingFile_ReusesItAndReportsTheSameFingerprint()
    {
        // A publisher's key must be stable across builds: a second call must not overwrite it,
        // because a new key silently breaks updates for everyone already installed.
        var path = Path.Combine(_dir, "falkforge-signing.pem");
        var first = PublisherKeyStore.EnsureKey(path, allowGeneration: true);
        var firstBytes = File.ReadAllBytes(path);

        var second = PublisherKeyStore.EnsureKey(path, allowGeneration: true);

        Assert.True(second.IsSuccess);
        Assert.False(second.Value.WasGenerated);
        Assert.Equal(first.Value.Fingerprint, second.Value.Fingerprint);
        Assert.Equal(firstBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void EnsureKey_FingerprintIsUppercaseSha256OfSpki()
    {
        // The baked pin is compared against exactly this value, so the computation must match
        // EngineTrustAnchor's: SHA-256 of SubjectPublicKeyInfo, uppercase hex, 64 characters.
        var path = Path.Combine(_dir, "falkforge-signing.pem");
        var result = PublisherKeyStore.EnsureKey(path, allowGeneration: true);

        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(path));
        var expected = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

        Assert.Equal(expected, result.Value.Fingerprint);
        Assert.Equal(64, result.Value.Fingerprint.Length);
    }

    [Fact]
    public void EnsureKey_GenerationForbidden_FailsAndWritesNothing()
    {
        // The CI guard: a runner that generates a fresh key each run publishes bundles whose
        // updates no installed copy can ever accept, and nothing says so. Refuse instead.
        var path = Path.Combine(_dir, "falkforge-signing.pem");

        var result = PublisherKeyStore.EnsureKey(path, allowGeneration: false);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void EnsureKey_GeneratedFileCarriesADoNotCommitHeader()
    {
        var path = Path.Combine(_dir, "falkforge-signing.pem");
        PublisherKeyStore.EnsureKey(path, allowGeneration: true);

        var text = File.ReadAllText(path);
        Assert.Contains("do not commit", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("back it up", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadPublicFingerprint_NoFile_ReturnsNull()
    {
        var path = Path.Combine(_dir, "falkforge-signing.pub");

        var result = PublisherKeyStore.ReadPublicFingerprint(path);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void WritePublicFingerprint_ThenRead_RoundTrips()
    {
        // The .pub file is what Task 7's ResolveForBuild reads to decide whether generating a new
        // key is legitimate (Part 1, section 1.3), so the round trip must reproduce the exact
        // 64-hex value, not a re-derived one.
        var path = Path.Combine(_dir, "falkforge-signing.pub");
        var fingerprint = Convert.ToHexString(SHA256.HashData("probe"u8));

        var written = PublisherKeyStore.WritePublicFingerprint(path, fingerprint);
        var read = PublisherKeyStore.ReadPublicFingerprint(path);

        Assert.True(written.IsSuccess);
        Assert.True(File.Exists(path));
        Assert.True(read.IsSuccess);
        Assert.Equal(fingerprint, read.Value);
    }

    [Fact]
    public void ReadPublicFingerprint_UnparseableFile_Fails()
    {
        // An unparseable .pub must fail loud rather than be treated as absent — treating it as
        // absent would silently reach the key-generating branch on a corrupted commit.
        var path = Path.Combine(_dir, "falkforge-signing.pub");
        File.WriteAllText(path, "not a fingerprint");

        var result = PublisherKeyStore.ReadPublicFingerprint(path);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }
}
