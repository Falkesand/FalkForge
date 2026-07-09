namespace FalkForge.Engine.Tests.Integrity;

using System.Security.AccessControl;
using System.Security.Principal;
using FalkForge.Engine.Integrity;
using Xunit;

/// <summary>
/// The persisted anti-downgrade/revocation store (C14 Stage 2, §6.2). It records the highest key-epoch
/// this machine has accepted and the fingerprints explicitly revoked, and advances monotonically only —
/// never backwards — so a replayed older-epoch release cannot roll the client's trust state back.
/// </summary>
public sealed class TrustStateStoreTests
{
    private static string TempStorePath() =>
        Path.Combine(Path.GetTempPath(), $"falk-trust-{Guid.NewGuid():N}", "trust-state.json");

    // The persistence tests exercise epoch/revocation logic, not ACL creation. In production the Trust
    // directory is provisioned once by an elevated install; subsequent advances write into the EXISTING
    // directory (whose restrictive ACL TrustStateStore leaves untouched). These tests pre-create the
    // directory so Advance's write succeeds under a non-elevated test host — the fresh-create ACL is
    // asserted separately by Advance_CreatesTrustDirectory_WithRestrictiveAcl_WindowsOnly.
    private static string PreProvisionedStorePath()
    {
        var path = TempStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    [Fact]
    public void Load_MissingFile_ReturnsFirstRunState()
    {
        var state = TrustStateStore.Load(TempStorePath());

        Assert.Equal(0, state.Epoch);
        Assert.Empty(state.RevokedFingerprints);
    }

    [Fact]
    public void Advance_FromFirstRun_PersistsEpochAndRevocations()
    {
        var path = PreProvisionedStorePath();
        try
        {
            var advance = TrustStateStore.Advance(path, epoch: 5, revoked: new[] { "AABB" });
            Assert.True(advance.IsSuccess, advance.IsFailure ? advance.Error.Message : null);

            var reloaded = TrustStateStore.Load(path);
            Assert.Equal(5, reloaded.Epoch);
            Assert.Contains("AABB", reloaded.RevokedFingerprints);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Advance_LowerEpoch_DoesNotRollBack_Monotonic()
    {
        var path = PreProvisionedStorePath();
        try
        {
            TrustStateStore.Advance(path, epoch: 5, revoked: []);
            TrustStateStore.Advance(path, epoch: 3, revoked: []); // stale/replay — must not lower

            Assert.Equal(5, TrustStateStore.Load(path).Epoch);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Advance_MergesRevocations_Union()
    {
        var path = PreProvisionedStorePath();
        try
        {
            TrustStateStore.Advance(path, epoch: 1, revoked: new[] { "AABB" });
            TrustStateStore.Advance(path, epoch: 2, revoked: new[] { "CCDD" });

            var state = TrustStateStore.Load(path);
            Assert.Contains("AABB", state.RevokedFingerprints);
            Assert.Contains("CCDD", state.RevokedFingerprints);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Advance_TwoElementRevocation_RecordsBothFingerprints()
    {
        // C14 Stage 3 FIX 3: a genuine 2-element revocation from one verified update must land BOTH
        // fingerprints in the store (the injective signed-bytes encoding guarantees the list arriving here
        // is the exact list the publisher signed, not a restructured single merged entry).
        var path = PreProvisionedStorePath();
        try
        {
            var advance = TrustStateStore.Advance(path, epoch: 4, revoked: new[] { "FP1", "FP2" });
            Assert.True(advance.IsSuccess, advance.IsFailure ? advance.Error.Message : null);

            var state = TrustStateStore.Load(path);
            Assert.Equal(4, state.Epoch);
            Assert.Contains("FP1", state.RevokedFingerprints);
            Assert.Contains("FP2", state.RevokedFingerprints);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Advance_CreatesTrustDirectory_WithRestrictiveAcl_WindowsOnly()
    {
        // C14 Stage 3 FIX 4: the per-machine store claims tamper-resistance, but a default-ACL directory
        // under %ProgramData% lets an unprivileged process edit it (roll back the epoch / clear
        // revocations). The store directory must be created with a restrictive DACL: SYSTEM +
        // Administrators FullControl, Users read-only (no standard-user write). ACL hardening is a Windows
        // concept, so this test no-ops elsewhere.
        if (!OperatingSystem.IsWindows())
            return;

        var path = TempStorePath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            // The write itself may fail if the test host runs as a pure standard user (the restrictive ACL
            // denies its write — the elevation follow-up); the directory + ACL are created regardless, which
            // is what this test asserts.
            TrustStateStore.Advance(path, epoch: 1, revoked: []);
            Assert.True(Directory.Exists(dir), "the Trust directory must be created");
            AssertRestrictiveDacl(dir);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AssertRestrictiveDacl(string dir)
    {
        var security = new DirectoryInfo(dir).GetAccessControl();
        var rules = security
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();

        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        // Inheritance is severed so ProgramData's broad default ACL cannot leak Users-write in.
        Assert.True(security.AreAccessRulesProtected, "the store DACL must not inherit the parent's broad ACL");

        // Administrators keep FullControl (the elevated writer).
        Assert.Contains(rules, r =>
            r.IdentityReference.Equals(admins)
            && r.AccessControlType == AccessControlType.Allow
            && r.FileSystemRights.HasFlag(FileSystemRights.FullControl));

        // Users (standard accounts) must NOT have a write-data grant (read-only is fine).
        Assert.DoesNotContain(rules, r =>
            r.IdentityReference.Equals(users)
            && r.AccessControlType == AccessControlType.Allow
            && r.FileSystemRights.HasFlag(FileSystemRights.WriteData));
    }

    private static void TryCleanup(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}
