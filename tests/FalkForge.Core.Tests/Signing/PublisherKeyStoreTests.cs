using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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

    // A fixed PUBLIC key (SubjectPublicKeyInfo, DER, base64), not a secret: nobody holds or ever held
    // the private half this SPKI would pair with in this test, it was generated once, offline, purely
    // to have a stable input, and the corresponding private key was discarded immediately after. It
    // and the fingerprint below were computed independently of PublisherKeyStore -- outside this test,
    // outside the production code -- so the assertion below pins the algorithm (uppercase-hex SHA-256
    // over the SPKI) against a value neither this test nor the code under test derived. The earlier
    // version of this test recomputed its "expected" value with the same production helper it was
    // checking, which proves only that the computation is deterministic, not that it is correct.
    private const string FixedPublicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEj0i3gS3eRP3DjdsEMwoqtqL2ompX" +
        "n2zAtPcvqKF4N9vx4eVB46mqpnaOVNwM9pN85gnptObA4KdxMQ8tp8tKJA==";

    private const string FixedPublicKeyExpectedFingerprint =
        "2AB0ACC68337B644FAF0F5C62A72A23B12A83855F2CF99E024CD364D7072D893";

    [Fact]
    public void ComputeFingerprint_FixedPublicKey_MatchesThePinnedValue()
    {
        var spki = Convert.FromBase64String(FixedPublicKeySpkiBase64);
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(spki, out _);

        var fingerprint = PublisherKeyStore.ComputeFingerprint(key);

        Assert.Equal(FixedPublicKeyExpectedFingerprint, fingerprint);
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

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void ReadPublicFingerprint_WrongLength_Fails(int length)
    {
        var path = Path.Combine(_dir, "falkforge-signing.pub");
        File.WriteAllText(path, "# header\n" + new string('A', length) + "\n");

        var result = PublisherKeyStore.ReadPublicFingerprint(path);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void ReadPublicFingerprint_LowercaseHex_Fails()
    {
        // The stored fingerprint is compared byte-for-byte (WritePublicFingerprint always writes
        // uppercase), so a lowercase value can only mean hand-edited or corrupted content.
        var path = Path.Combine(_dir, "falkforge-signing.pub");
        File.WriteAllText(path, "# header\n" + new string('a', 64) + "\n");

        var result = PublisherKeyStore.ReadPublicFingerprint(path);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void EnsureKey_NewlyGeneratedKeyOnASingleUserOwnedTempDir_CarriesNoWarning()
    {
        // The temp directory this test writes into is the current user's own, so a correctly
        // restricted key file must produce no advisory warning.
        var path = Path.Combine(_dir, "falkforge-signing.pem");

        var result = PublisherKeyStore.EnsureKey(path, allowGeneration: true);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(result.Value.Warning);
    }

    [Fact]
    public void EnsureKey_NoFile_RestrictsTheWrittenFileToTheCurrentUserWindows()
    {
        // The key must never be readable by anyone but the current user. This checks the end state
        // EnsureKey leaves on disk (owner + DACL), the outcome the TOCTOU fix in
        // CreateRestrictedFileStreamWindows exists to guarantee with no window of exposure.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows-only: exercises Windows ACL/security APIs.");
            return; // Unreachable (Skip throws) — kept so the CA1416 platform-guard analysis sees the branch exit.
        }

        var path = Path.Combine(_dir, "falkforge-signing.pem");
        var result = PublisherKeyStore.EnsureKey(path, allowGeneration: true);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var currentUser = WindowsIdentity.GetCurrent().User!;
        var security = new FileInfo(path).GetAccessControl();
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var rules = security
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();

        Assert.True(PublisherKeyStore.IsRestrictedToCurrentUser(
            currentUser, owner, security.AreAccessRulesProtected, rules));
    }

    [Fact]
    public void EnsureKey_NoFile_RestrictsTheWrittenFileModeOnUnix()
    {
        // Off Windows the old code applied no restriction at all; the key landed at the process
        // umask, which is commonly group- or world-readable.
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix-only: exercises POSIX file mode bits.");
            return;
        }

        var path = Path.Combine(_dir, "falkforge-signing.pem");
        var result = PublisherKeyStore.EnsureKey(path, allowGeneration: true);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void IsRestrictedToCurrentUser_OwnerIsSomeoneElse_ReturnsFalse()
    {
        // The attack this defends against: an attacker creates the file (becoming its owner), then
        // grants the victim a FullControl Allow ACE. The ACE alone looks fine; only the owner check
        // catches that the attacker still holds implicit WRITE_DAC/WRITE_OWNER and can rewrite the
        // ACL back at will. No elevation is needed to construct this: the "foreign" owner is a
        // fabricated SID, never actually applied to a real file.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows-only: exercises Windows ACL/security APIs.");
            return;
        }

        var currentUser = WindowsIdentity.GetCurrent().User!;
        var foreignOwner = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var rules = new[]
        {
            new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, AccessControlType.Allow),
        };

        var result = PublisherKeyStore.IsRestrictedToCurrentUser(
            currentUser, foreignOwner, areAccessRulesProtected: true, rules);

        Assert.False(result);
    }

    [Fact]
    public void IsRestrictedToCurrentUser_ForeignAllowAce_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows-only: exercises Windows ACL/security APIs.");
            return;
        }

        var currentUser = WindowsIdentity.GetCurrent().User!;
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var rules = new[]
        {
            new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, AccessControlType.Allow),
            new FileSystemAccessRule(everyone, FileSystemRights.Read, AccessControlType.Allow),
        };

        var result = PublisherKeyStore.IsRestrictedToCurrentUser(
            currentUser, currentUser, areAccessRulesProtected: true, rules);

        Assert.False(result);
    }

    [Fact]
    public void IsRestrictedToCurrentUser_OwnerAndSoleAceAreCurrentUser_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows-only: exercises Windows ACL/security APIs.");
            return;
        }

        var currentUser = WindowsIdentity.GetCurrent().User!;
        var rules = new[]
        {
            new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, AccessControlType.Allow),
        };

        var result = PublisherKeyStore.IsRestrictedToCurrentUser(
            currentUser, currentUser, areAccessRulesProtected: true, rules);

        Assert.True(result);
    }

    [Fact]
    public void IsRestrictedToCurrentUserWindows_UnreadableAcl_FailsClosed()
    {
        // A path whose ACL cannot even be read (here, because nothing exists at it — GetAccessControl
        // throws FileNotFoundException, an IOException) must be reported as NOT restricted. The old
        // catch block returned true, so a file the check could not examine at all was waved through
        // as conforming.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows-only: exercises Windows ACL/security APIs.");
            return;
        }

        var path = Path.Combine(_dir, "does-not-exist.pem");

        var result = PublisherKeyStore.IsRestrictedToCurrentUserWindows(path);

        Assert.False(result);
    }
}
