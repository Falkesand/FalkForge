namespace FalkForge.Engine.Tests.Integrity;

using System.Security.AccessControl;
using System.Security.Principal;
using FalkForge.Engine.Protocol.Integrity;
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

    // ── C16: ACL validation / anti-squat ──────────────────────────────────────

    [Fact]
    public void LoadValidated_AbsentStore_ReturnsFirstRunState()
    {
        // An absent store directory is a first run, not a squat — LoadValidated must succeed
        // with epoch 0 / no revocations (the safe pre-rotation baseline), never fail closed.
        var result = TrustStateStore.LoadValidated(TempStorePath());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(0, result.Value.Epoch);
        Assert.Empty(result.Value.RevokedFingerprints);
    }

    [Fact]
    public void EnsureSecuredDirectory_ResetsLooseAcl_ToConforming_WindowsOnly()
    {
        // Anti-squat: an attacker pre-creates %ProgramData%\FalkForge\Trust with a loose ACL (Users
        // writable, inheritance intact) BEFORE first run to keep the store writable and roll back the
        // epoch / clear revocations at will. On use the elevated write-path must RESET the DACL to the
        // restrictive shape rather than trust the attacker-provisioned directory.
        if (!OperatingSystem.IsWindows())
            return;

        var path = TempStorePath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            CreateLooseDirectory(dir);
            Assert.False(TrustStateStore.IsDirectoryAclConforming(dir),
                "a loose pre-created directory must be detected as non-conforming");

            var result = TrustStateStore.EnsureSecuredDirectory(dir);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

            Assert.True(TrustStateStore.IsDirectoryAclConforming(dir),
                "EnsureSecuredDirectory must reset a loose directory to the conforming DACL");
            AssertRestrictiveDacl(dir);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void LoadValidated_LooseDirectory_RefusesToTrust_WindowsOnly()
    {
        // A store directory whose ACL an unprivileged process could have tampered must NOT be trusted for
        // anti-downgrade decisions: LoadValidated fails closed (SecurityError) so the caller refuses the
        // update rather than silently trusting an attacker-writable epoch/revocation set.
        if (!OperatingSystem.IsWindows())
            return;

        var path = TempStorePath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            CreateLooseDirectory(dir);
            File.WriteAllText(path, "{\"schemaVersion\":1,\"epoch\":5,\"revokedFingerprints\":[]}");

            var result = TrustStateStore.LoadValidated(path);

            Assert.True(result.IsFailure, "a loose store directory must not be trusted");
            Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void LoadValidated_ConformingDirectory_LoadsPersistedState_WindowsOnly()
    {
        // A conforming (hardened) store directory is trusted: LoadValidated returns the persisted epoch.
        // The file is written BEFORE the DACL is hardened so the non-elevated test host can seed it; after
        // hardening, Users retain read (ReadAndExecute) so the load still succeeds.
        if (!OperatingSystem.IsWindows())
            return;

        var path = TempStorePath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, "{\"schemaVersion\":1,\"epoch\":7,\"revokedFingerprints\":[\"AABB\"]}");

            var secured = TrustStateStore.EnsureSecuredDirectory(dir);
            Assert.True(secured.IsSuccess, secured.IsFailure ? secured.Error.Message : null);

            var result = TrustStateStore.LoadValidated(path);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            Assert.Equal(7, result.Value.Epoch);
            Assert.Contains("AABB", result.Value.RevokedFingerprints);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateLooseDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        var security = new DirectoryInfo(dir).GetAccessControl();
        // Inheritance intact (NOT protected) + an explicit Users FullControl grant = the loose shape an
        // unprivileged squatter would leave to keep the store writable.
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            users, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).SetAccessControl(security);
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
