namespace FalkForge.Engine.Elevation.Tests.Commands;

using System.Security.Cryptography;
using System.Text;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

/// <summary>
/// The elevated <c>TrustStateAdvance</c> command (C16): the only whitelisted operation that advances the
/// ACL-protected anti-downgrade/revocation store. A same-user caller on this pipe must not be able to jam
/// the epoch (a denial of service on the require-signed update path) or inject a revocation (bricking a
/// publisher key), so the command now requires a publisher-signed manifest it verifies against its OWN baked
/// key set and takes the epoch + revocations from the VERIFIED envelope — never from the wire. These tests
/// encode that an advance not backed by a trusted signature, an empty baked set, and an old raw-int-format
/// payload are all refused with the store left untouched, and that a properly-signed advance is applied.
/// </summary>
public sealed class TrustStateAdvanceCommandTests
{
    private const string PackageId = "App.Main";

    private static string TempStorePath() =>
        Path.Combine(Path.GetTempPath(), $"falk-trustcmd-{Guid.NewGuid():N}", "trust-state.json");

    private static TrustStateAdvanceCommand Command(string storePath, IReadOnlySet<string> trusted) =>
        new(storePath, trusted, SignedManifestPayload.NoRoles, SignedManifestPayload.NoPqCompanions);

    private static TrustStateAdvanceCommand Command(
        string storePath, IReadOnlySet<string> trusted, IReadOnlyDictionary<string, TrustRole> roles) =>
        new(storePath, trusted, roles, SignedManifestPayload.NoPqCompanions);

    // The store advances only via TrustStateStore.Advance, which persists a file. On any rejection before the
    // write, no file is created — so "store not advanced" is provable by the absence of a persisted epoch.
    private static void AssertStoreNotAdvanced(string path)
    {
        var state = TrustStateStore.Load(path);
        Assert.Equal(0, state.Epoch);
        Assert.Empty(state.RevokedFingerprints);
    }

    [Fact]
    public void Execute_AdvanceSignedByUntrustedKey_Rejected_StoreNotAdvanced()
    {
        // #1: the epoch + revocation ride inside an envelope signed by a key the companion does NOT trust.
        // The signature does not anchor to the baked set, so the quorum is unsatisfied (INT010) and the
        // store never advances — a caller cannot jam the epoch or inject a revocation without a trusted key.
        var path = TempStorePath();
        using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.AdvanceManifestJson(
            PackageId, new string('A', 64), epoch: 9, revoked: ["DEADBEEF"], stranger);
        var payload = TrustAdvancePayload.Serialize(manifestJson);

        // Trust only the publisher; the advance is signed by the stranger.
        var result = Command(path, SignedManifestPayload.TrustedSet(publisher)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
        AssertStoreNotAdvanced(path);
    }

    [Fact]
    public void Execute_EmptyBakedSet_Rejected_StoreNotAdvanced()
    {
        // #2: an empty baked set on the require-signed path fails closed with INT009 — a required signature
        // with no trust anchor cannot establish authorship. Mirrors MsiInstall's empty-set behaviour.
        var path = TempStorePath();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.AdvanceManifestJson(
            PackageId, new string('A', 64), epoch: 3, revoked: [], key);
        var payload = TrustAdvancePayload.Serialize(manifestJson);

        var result = Command(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT009", result.Error.Message, StringComparison.Ordinal);
        AssertStoreNotAdvanced(path);
    }

    [Fact]
    public void Execute_UnsignedManifest_Rejected_StoreNotAdvanced()
    {
        // A manifest with no signature envelope on the require-signed path is INT007.
        var path = TempStorePath();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.ManifestJson(
            envelopeEntries: [], packages: [(PackageId, new string('A', 64))], preUI: [],
            companionSha256: null, signingKey: null);
        var payload = TrustAdvancePayload.Serialize(manifestJson);

        var result = Command(path, SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT007", result.Error.Message, StringComparison.Ordinal);
        AssertStoreNotAdvanced(path);
    }

    [Fact]
    public void Execute_OldRawIntFormatPayload_Rejected_StoreNotAdvanced()
    {
        // #3 (the folded fail-closed wire rule): the PRE-FIX wire format was a bare epoch + revocation ints
        // with no signature. That format sent to the new companion must be REFUSED — no fallback to the old
        // parser, and a missing manifest is never a legacy allow-through. The new payload carries a
        // magic + version prefix an old raw-int blob cannot satisfy.
        var path = TempStorePath();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var oldFormatPayload = SerializeOldRawIntFormat(epoch: 7, revoked: ["AABB"]);

        var result = Command(path, SignedManifestPayload.TrustedSet(key)).Execute(oldFormatPayload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("old-format", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        AssertStoreNotAdvanced(path);
    }

    [Fact]
    public void Execute_ProperlySignedAdvance_IsApplied_OrFailsLoudOnElevatedWrite()
    {
        // #4: a genuine epoch advance (stored 0 -> 1) resolves as a KeyChange, which needs a Release +
        // Recovery quorum (two distinct keys). Signed by both trusted keys, verification passes and the
        // epoch is taken from the verified envelope. The store directory is SYSTEM/Admins-hardened, so on an
        // elevated host the write lands (assert epoch 1); on a non-elevated CI host the hardened write is
        // denied and the command fails loud on the WRITE — never on verification, and never a silent no-op.
        var path = TempStorePath();
        using var release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var recovery = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.AdvanceManifestJson(
            PackageId, new string('A', 64), epoch: 1, revoked: [], release, recovery);
        var payload = TrustAdvancePayload.Serialize(manifestJson);
        var command = Command(
            path,
            SignedManifestPayload.TrustedSet(release, recovery),
            SignedManifestPayload.Roles((release, TrustRole.Release), (recovery, TrustRole.Recovery)));

        try
        {
            var result = command.Execute(payload);

            if (result.IsSuccess)
            {
                var state = TrustStateStore.Load(path);
                Assert.Equal(1, state.Epoch);
            }
            else
            {
                // Verification passed; only the elevated write could have failed. Prove it was the write,
                // not a trust rejection, by ruling out every verification failure code/message.
                var message = result.Error.Message;
                Assert.DoesNotContain("INT0", message, StringComparison.Ordinal);
                Assert.DoesNotContain("old-format", message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("no signed manifest", message, StringComparison.OrdinalIgnoreCase);
                Assert.True(
                    message.Contains("trust store directory", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("persist trust state", StringComparison.OrdinalIgnoreCase),
                    $"Expected an elevated-write failure, got: {message}");
            }
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Name_IsTrustStateAdvance()
    {
        Assert.Equal("TrustStateAdvance", new TrustStateAdvanceCommand(TempStorePath()).Name);
    }

    // The pre-fix wire format: [epoch:int32-LE][count:int32-LE]{ [len:int32-LE][utf8] } x count. Rebuilt
    // inline so the test proves the new companion rejects the exact bytes an old engine would have sent.
    private static byte[] SerializeOldRawIntFormat(int epoch, string[] revoked)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(epoch);
            writer.Write(revoked.Length);
            foreach (var fingerprint in revoked)
            {
                var bytes = Encoding.UTF8.GetBytes(fingerprint);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    private static void TryCleanup(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            TestTemp.TryDelete(dir);
    }
}
