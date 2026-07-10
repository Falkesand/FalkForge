namespace FalkForge.Engine.Protocol.Tests.Integrity;

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

/// <summary>
/// Pure in-memory tests for the trust-store directory ACL conformance rule and the restrictive-DACL reset
/// transform (C16 HIGH remediation). They exercise <see cref="TrustStateStore.IsAclConforming"/> and
/// <see cref="TrustStateStore.ApplyRestrictiveDacl"/> against fabricated descriptors, so the security-critical
/// decision is verified WITHOUT touching the filesystem and WITHOUT requiring elevation (an in-memory
/// <see cref="DirectorySecurity"/> lets us set an Administrators owner or a foreign write ACE that a real
/// on-disk directory could only be given under an elevated token). The adversarial gap these close: the old
/// check blacklisted write only for four BROAD SIDs (Users/World/Authenticated/Interactive) and never checked
/// the owner, so a squatter who owned the directory and granted write to their OWN specific SID passed as
/// "conforming".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrustStoreAclLogicTests
{
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    // A specific, non-privileged user SID standing in for "the attacker's own SID" — NOT one of the broad
    // groups the old blacklist covered, which is the whole point.
    private static readonly SecurityIdentifier NonAdminUser =
        new("S-1-5-21-1111111111-2222222222-3333333333-1001");

    private const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    private static DirectorySecurity Descriptor(SecurityIdentifier owner, bool isProtected)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected, preserveInheritance: false);
        security.SetOwner(owner);
        return security;
    }

    private static void Grant(DirectorySecurity s, SecurityIdentifier sid, FileSystemRights rights) =>
        s.AddAccessRule(new FileSystemAccessRule(sid, rights, Inherit, PropagationFlags.None, AccessControlType.Allow));

    private static bool Conforming(DirectorySecurity s) => TrustStateStore.IsAclConforming(
        s.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier,
        s.AreAccessRulesProtected,
        s.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)));

    [Fact]
    public void AdminOwner_PrivilegedWritersOnly_UsersReadOnly_IsConforming()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var s = Descriptor(Admins, isProtected: true);
        Grant(s, System, FileSystemRights.FullControl);
        Grant(s, Admins, FileSystemRights.FullControl);
        Grant(s, Users, FileSystemRights.ReadAndExecute);

        Assert.True(Conforming(s), "the canonical hardened shape (admin owner, only SYSTEM/Admins write) must conform");
    }

    [Fact]
    public void AdminOwner_TargetedNonAdminWriter_IsNotConforming()
    {
        // The core regression: a hardened DACL PLUS one write grant to a specific non-admin SID. The owner is
        // Administrators (so ONLY the writer whitelist can be the discriminator — this isolates fix #1 from
        // the owner check). The old broad-SID blacklist returned true here; the whitelist must return false.
        if (!OperatingSystem.IsWindows())
            return;

        var s = Descriptor(Admins, isProtected: true);
        Grant(s, System, FileSystemRights.FullControl);
        Grant(s, Admins, FileSystemRights.FullControl);
        Grant(s, Users, FileSystemRights.ReadAndExecute);
        Grant(s, NonAdminUser, FileSystemRights.Modify); // attacker's own-SID write

        Assert.False(Conforming(s),
            "a write-class grant to a specific non-admin SID must fail conformance (writer whitelist, not blacklist)");
    }

    [Fact]
    public void NonAdminOwner_OtherwiseHardened_IsNotConforming()
    {
        // Isolates fix #2: a spotless DACL but owned by a non-admin principal (implicit WRITE_DAC).
        if (!OperatingSystem.IsWindows())
            return;

        var s = Descriptor(NonAdminUser, isProtected: true);
        Grant(s, System, FileSystemRights.FullControl);
        Grant(s, Admins, FileSystemRights.FullControl);
        Grant(s, Users, FileSystemRights.ReadAndExecute);

        Assert.False(Conforming(s), "a non-admin owner must fail conformance (owner holds implicit WRITE_DAC)");
    }

    [Fact]
    public void SystemOwner_IsAcceptedAsPrivileged()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var s = Descriptor(System, isProtected: true);
        Grant(s, System, FileSystemRights.FullControl);
        Grant(s, Admins, FileSystemRights.FullControl);
        Grant(s, Users, FileSystemRights.ReadAndExecute);

        Assert.True(Conforming(s), "SYSTEM is a privileged owner and must conform");
    }

    [Fact]
    public void NotProtected_IsNotConforming()
    {
        // The inheritance-severed requirement is retained: an un-protected DACL lets ProgramData's broad
        // default ACL leak a Users-write grant back in.
        if (!OperatingSystem.IsWindows())
            return;

        var s = Descriptor(Admins, isProtected: false);
        Grant(s, System, FileSystemRights.FullControl);
        Grant(s, Admins, FileSystemRights.FullControl);

        Assert.False(Conforming(s), "a non-protected (inheriting) DACL must fail conformance");
    }

    [Fact]
    public void BroadUsersWriteGrant_StillCaught()
    {
        // The original broad-SID case must still be rejected under the whitelist.
        if (!OperatingSystem.IsWindows())
            return;

        var s = Descriptor(Admins, isProtected: true);
        Grant(s, System, FileSystemRights.FullControl);
        Grant(s, Admins, FileSystemRights.FullControl);
        Grant(s, Users, FileSystemRights.Modify); // broad write

        Assert.False(Conforming(s), "a broad Users write grant must still fail conformance");
    }

    [Fact]
    public void ApplyRestrictiveDacl_SeizesOwnership_PurgesForeignWriter_ResultConforms()
    {
        // Fix #3: resetting a squatted descriptor (non-admin owner + own-SID write grant) must seize
        // ownership to Administrators, purge the attacker's ACE, and leave a conforming shape.
        if (!OperatingSystem.IsWindows())
            return;

        var s = Descriptor(NonAdminUser, isProtected: false);
        Grant(s, NonAdminUser, FileSystemRights.FullControl); // squatter's own-SID grant
        Grant(s, Users, FileSystemRights.FullControl);        // and a broad grant for good measure

        TrustStateStore.ApplyRestrictiveDacl(s);

        var owner = s.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        Assert.Equal(Admins, owner);

        var rules = s.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToList();

        Assert.DoesNotContain(rules, r => r.IdentityReference.Equals(NonAdminUser));
        Assert.Contains(rules, r => r.IdentityReference.Equals(System) && r.FileSystemRights.HasFlag(FileSystemRights.FullControl));
        Assert.Contains(rules, r => r.IdentityReference.Equals(Admins) && r.FileSystemRights.HasFlag(FileSystemRights.FullControl));
        // Users retain read but NOT write.
        Assert.DoesNotContain(rules, r => r.IdentityReference.Equals(Users) && r.FileSystemRights.HasFlag(FileSystemRights.WriteData));

        Assert.True(Conforming(s), "a reset squatted descriptor must be conforming");
    }
}
