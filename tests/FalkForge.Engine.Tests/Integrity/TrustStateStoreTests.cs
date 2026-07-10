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

    [Fact]
    public void Advance_AbsurdEpoch_Rejected_StoreNotJammed_NormalEpochStillAdvances()
    {
        // Absolute epoch cap: a compromised/buggy caller must not be able to saturate the stored epoch (e.g.
        // int.MaxValue), which — because the store is monotonic and INT008 rejects any lower epoch — would
        // permanently lock out EVERY subsequent legitimate update. The advance must be REFUSED (fail loud),
        // the store left untouched, and a normal forward epoch must still advance.
        var path = PreProvisionedStorePath();
        try
        {
            Assert.True(TrustStateStore.Advance(path, epoch: 5, revoked: []).IsSuccess);

            var absurd = TrustStateStore.Advance(path, epoch: int.MaxValue, revoked: []);
            Assert.True(absurd.IsFailure, "an out-of-range epoch must be rejected");
            Assert.Equal(ErrorKind.SecurityError, absurd.Error.Kind);
            Assert.Equal(5, TrustStateStore.Load(path).Epoch); // untouched — NOT jammed to the cap

            var forward = TrustStateStore.Advance(path, epoch: 6, revoked: []);
            Assert.True(forward.IsSuccess, forward.IsFailure ? forward.Error.Message : null);
            Assert.Equal(6, TrustStateStore.Load(path).Epoch); // a normal epoch still advances
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Advance_EpochAtCap_IsAllowed_JustAboveCap_IsRejected()
    {
        var path = PreProvisionedStorePath();
        try
        {
            Assert.True(TrustStateStore.Advance(path, epoch: TrustStateStore.MaxEpoch, revoked: []).IsSuccess);
            Assert.Equal(TrustStateStore.MaxEpoch, TrustStateStore.Load(path).Epoch);

            Assert.True(TrustStateStore.Advance(path, epoch: TrustStateStore.MaxEpoch + 1, revoked: []).IsFailure);
        }
        finally
        {
            TryCleanup(path);
        }
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
        //
        // The reset now also SEIZES ownership to Administrators, which requires an elevated token
        // (SeRestorePrivilege). Skip cleanly when the test host is not elevated — in production this path
        // only ever runs in the elevated companion.
        if (!OperatingSystem.IsWindows() || !IsElevatedWindows())
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
        // hardening, Users retain read (ReadAndExecute) so the load still succeeds. EnsureSecuredDirectory
        // now seizes ownership to Administrators (the directory was created owned by the test user), which
        // requires an elevated token — skip cleanly when the host is not elevated.
        if (!OperatingSystem.IsWindows() || !IsElevatedWindows())
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

    [Fact]
    public void IsDirectoryAclConforming_TargetedNonBroadWriter_Rejected_WindowsOnly()
    {
        // Adversarial squat that the ORIGINAL broad-SID blacklist missed: a standard user pre-creates the
        // Trust directory (becoming its owner) and sets a PROTECTED DACL that grants write to their OWN
        // specific SID — not a broad group (Users/Everyone/Authenticated Users/Interactive). The old
        // conformance check only blacklisted those four broad SIDs, so it returned TRUE and the attacker's
        // epoch/revocation store was trusted. The whitelist rule must reject any write-class grant held by a
        // principal other than SYSTEM / BUILTIN\Administrators, so a targeted own-SID write is non-conforming.
        if (!OperatingSystem.IsWindows())
            return;

        var path = TempStorePath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            CreateProtectedDirectoryWithWriter(dir, WindowsIdentity.GetCurrent().User!);

            Assert.False(TrustStateStore.IsDirectoryAclConforming(dir),
                "a protected DACL granting write to a specific non-admin SID (targeted squat) must be non-conforming");
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void IsDirectoryAclConforming_NonAdminOwner_Rejected_WindowsOnly()
    {
        // Even with a spotless DACL (SYSTEM/Admins FullControl, Users read-only, inheritance severed, no
        // foreign write ACE), a directory OWNED by a non-admin principal is non-conforming: the owner holds
        // implicit WRITE_DAC and can rewrite the DACL at any moment. The old check ignored the owner entirely.
        if (!OperatingSystem.IsWindows())
            return;

        // This test builds a directory owned by a genuinely non-admin principal (the current account's
        // individual SID). If the host process itself runs AS SYSTEM or under the Administrators group SID,
        // no non-admin identity exists to own the directory, so the precondition cannot be constructed —
        // skip cleanly (the pure-logic TrustStoreAclLogicTests.NonAdminOwner_OtherwiseHardened_IsNotConforming
        // covers the rule with fabricated descriptors regardless of host identity).
        var currentUser = WindowsIdentity.GetCurrent().User!;
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        if (currentUser.Equals(systemSid) || currentUser.Equals(adminsSid))
            return;

        var path = TempStorePath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            CreateProtectedHardenedButUserOwnedDirectory(dir, currentUser);

            Assert.False(TrustStateStore.IsDirectoryAclConforming(dir),
                "a directory owned by a non-admin principal must be non-conforming (owner has implicit WRITE_DAC)");
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateProtectedDirectoryWithWriter(string dir, SecurityIdentifier writer)
    {
        Directory.CreateDirectory(dir);
        var security = new DirectoryInfo(dir).GetAccessControl();
        // Inheritance severed (protected) + an otherwise-hardened DACL, but with a targeted own-SID write
        // grant: exactly the shape a squatter would leave to keep the store writable while passing the old
        // broad-SID-only blacklist. The current test user owns the directory (they created it).
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            writer, FileSystemRights.Modify, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).SetAccessControl(security);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateProtectedHardenedButUserOwnedDirectory(string dir, SecurityIdentifier owner)
    {
        Directory.CreateDirectory(dir);
        var security = new DirectoryInfo(dir).GetAccessControl();
        // A hardened DACL (protected, SYSTEM/Admins FullControl, Users read-only, no foreign writer) but the
        // directory is owned by a non-admin principal — the only defect is the owner. The owner is set
        // EXPLICITLY rather than relying on the creation default: under an elevated administrator token
        // (e.g. a GitHub-hosted Windows runner) a freshly created directory is owned by BUILTIN\Administrators,
        // which would make it legitimately conforming and defeat the intent of this negative test.
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).SetAccessControl(security);
    }

    [Fact]
    public void LoadValidated_NonConforming_ErrorText_GivesAccurateRecoveryGuidance_WindowsOnly()
    {
        // The fail-closed message must give ACCURATE recovery guidance. The old text ("Reset it with an
        // elevated install") was misleading: a normal (non-require-signed) install never resets the store,
        // and this non-elevated read path cannot self-heal. The corrected text must name the administrator
        // recovery and must not carry the old misleading phrasing.
        if (!OperatingSystem.IsWindows())
            return;

        var path = TempStorePath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            CreateLooseDirectory(dir);
            File.WriteAllText(path, "{\"schemaVersion\":1,\"epoch\":5,\"revokedFingerprints\":[]}");

            var result = TrustStateStore.LoadValidated(path);

            Assert.True(result.IsFailure, "a non-conforming store must fail closed");
            Assert.Contains("administrator", result.Error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Reset it with an elevated install", result.Error.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsElevatedWindows()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
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
