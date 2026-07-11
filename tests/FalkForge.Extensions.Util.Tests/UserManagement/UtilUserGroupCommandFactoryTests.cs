using System.Text;
using FalkForge.Extensibility;
using FalkForge.Extensions.Util;
using FalkForge.Extensions.Util.UserManagement;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.UserManagement;

/// <summary>
/// Unit-level proof that <see cref="UtilUserGroupCommandFactory"/> translates User/Group models into the
/// right <see cref="ExecutionStep"/>s: create-order, the secure credential channel, reverse-ordered
/// uninstall removals, domain-reference scoping, and — critically, since the steps run as SYSTEM —
/// injection-inert single-quoting of author-supplied names.
/// </summary>
public sealed class UtilUserGroupCommandFactoryTests
{
    [Fact]
    public void Groups_ThenUsers_ThenMemberships_AreOrderedForInstall()
    {
        var group = new GroupModel { Name = "Ops" };
        var user = new UserModel { Name = "svc", Password = "P@ssw0rd!", Groups = ["Ops"] };

        var steps = UtilUserGroupCommandFactory.BuildSteps([group], [user]);
        var ids = steps.Select(s => s.Id).ToList();

        int g = ids.IndexOf("UGrp_Ops");
        int u = ids.IndexOf("UUsr_svc");
        int m = ids.IndexOf("UMem_svc_Ops");
        Assert.True(g >= 0 && u >= 0 && m >= 0, "expected group, user and membership steps");
        Assert.True(g < u && u < m, "install order must be group -> user -> membership");
    }

    [Fact]
    public void UserWithPasswordProperty_UsesSecureChannel_AndReportsHiddenProperties()
    {
        var user = new UserModel { Name = "svc", PasswordProperty = "USERPASSWORD" };

        var step = Single(UtilUserGroupCommandFactory.BuildSteps([], [user]), "UUsr_svc");
        Assert.Equal("[USERPASSWORD]", step.CustomActionData);
        Assert.Contains("[CustomActionData]", step.InstallCommand, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-SecureString", Decode(step.InstallCommand), StringComparison.Ordinal);

        var hidden = UtilUserGroupCommandFactory.CollectHiddenPropertyNames([user]);
        Assert.Contains("USERPASSWORD", hidden);
        Assert.Contains("UUsr_svc", hidden);
    }

    [Fact]
    public void UserWithoutCredential_UpdateIfExists_CarriesNoCustomActionData()
    {
        var user = new UserModel { Name = "svc", Password = null, UpdateIfExists = true };

        var step = Single(UtilUserGroupCommandFactory.BuildSteps([], [user]), "UUsr_svc");
        Assert.Null(step.CustomActionData);
        Assert.Empty(UtilUserGroupCommandFactory.CollectHiddenPropertyNames([user]));
    }

    [Fact]
    public void EvilGroupName_IsSingleQuotedInert_NotAbleToBreakOut()
    {
        // An apostrophe is legal in a Windows account name, so it survives validation and reaches the
        // SYSTEM-context script. It MUST be doubled inside the single-quoted literal, never left able to
        // terminate the string and hand control to the parser.
        var group = new GroupModel { Name = "a'b" };

        var step = Single(UtilUserGroupCommandFactory.BuildSteps([group], []), "UGrp_a_b");
        string script = Decode(step.InstallCommand);
        Assert.Contains("'a''b'", script, StringComparison.Ordinal);   // correctly escaped
        Assert.DoesNotContain("'a'b'", script, StringComparison.Ordinal); // never a break-out
    }

    [Fact]
    public void DomainUser_IsNotCreated_ButMembershipUsesQualifiedName()
    {
        var user = new UserModel { Name = "svc", Domain = "CONTOSO", Password = null, Groups = ["Ops"] };

        var steps = UtilUserGroupCommandFactory.BuildSteps([], [user]);
        Assert.DoesNotContain(steps, s => s.Id == "UUsr_svc"); // domain account is a reference, never created
        var membership = Single(steps, "UMem_svc_Ops");
        Assert.Contains(@"CONTOSO\svc", Decode(membership.InstallCommand), StringComparison.Ordinal);
    }

    [Fact]
    public void RemovalSteps_AreOrderedReverse_MembershipThenUserThenGroup()
    {
        var group = new GroupModel { Name = "Ops", RemoveOnUninstall = true };
        var user = new UserModel { Name = "svc", Password = "P@ssw0rd!", Groups = ["Ops"], RemoveOnUninstall = true };

        var steps = UtilUserGroupCommandFactory.BuildSteps([group], [user]);
        var ids = steps.Select(s => s.Id).ToList();

        int m = ids.IndexOf("UMemD_svc_Ops");
        int u = ids.IndexOf("UUsrD_svc");
        int g = ids.IndexOf("UGrpD_Ops");
        Assert.True(m >= 0 && u >= 0 && g >= 0, "expected membership, user and group removal steps");
        Assert.True(m < u && u < g, "uninstall order (list position) must be membership -> user -> group");
    }

    [Fact]
    public void NewUser_WithNoResolvedPassword_FailsLoud_NeverCreatesPasswordlessAccount()
    {
        // An UpdateIfExists user with no credential still creates a NEW account when absent at run time. The
        // create branch must fail loud instead of falling back to -NoPassword (a passwordless SYSTEM account).
        var user = new UserModel { Name = "svc", Password = null, UpdateIfExists = true };

        var step = Single(UtilUserGroupCommandFactory.BuildSteps([], [user]), "UUsr_svc");
        string script = Decode(step.InstallCommand);
        Assert.DoesNotContain("-NoPassword", script, StringComparison.Ordinal);
        Assert.Contains("throw", script, StringComparison.Ordinal);
        Assert.Contains("without a password", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupCreate_IsIdempotent_AdoptsPreExisting_WithoutThrowing()
    {
        var group = new GroupModel { Name = "Ops" }; // UpdateIfExists = false

        var step = Single(UtilUserGroupCommandFactory.BuildSteps([group], []), "UGrp_Ops");
        string script = Decode(step.InstallCommand);
        // Never fails on collision — a create that threw on a pre-existing group would TRIGGER the
        // already-queued rollback and delete that pre-existing group. It adopts instead.
        Assert.DoesNotContain("already exists", script, StringComparison.Ordinal);
        Assert.Contains("Get-LocalGroup -Name $__g", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupCreate_Rollback_IsMarkerGated_OnAnAdminOnlyRegistryStore()
    {
        // Both modes get a rollback, but it is gated on a per-run marker the create writes only when it
        // actually creates the group — so a pre-existing (adopted) group is never deleted on rollback. The
        // marker is an HKLM value (admin/SYSTEM-writable only), not a world-writable temp file, so a local
        // non-admin cannot plant it to weaponise the SYSTEM rollback.
        foreach (bool update in new[] { false, true })
        {
            var group = new GroupModel { Name = "Ops", UpdateIfExists = update };
            var step = Single(UtilUserGroupCommandFactory.BuildSteps([group], []), "UGrp_Ops");
            Assert.NotNull(step.RollbackCommand);
            string rb = Decode(step.RollbackCommand!);
            Assert.Contains("Get-ItemProperty -Path $__mkey", rb, StringComparison.Ordinal);
            Assert.Contains("HKLM:\\SOFTWARE\\FalkForge\\UserGroupMarkers", rb, StringComparison.Ordinal);
            Assert.Contains("Remove-LocalGroup", rb, StringComparison.Ordinal);

            // The create writes the marker only in the create-new branch, and only into HKLM.
            string create = Decode(step.InstallCommand);
            Assert.Contains("New-ItemProperty -Path $__mkey", create, StringComparison.Ordinal);
            Assert.DoesNotContain("$env:TEMP", create, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UserAndMembership_Rollbacks_AreMarkerGated()
    {
        var user = new UserModel { Name = "svc", Password = "P@ssw0rd!", Groups = ["Ops"] };
        var steps = UtilUserGroupCommandFactory.BuildSteps([new GroupModel { Name = "Ops" }], [user]);

        string userRb = Decode(Single(steps, "UUsr_svc").RollbackCommand!);
        Assert.Contains("Get-ItemProperty -Path $__mkey", userRb, StringComparison.Ordinal);
        Assert.Contains("Remove-LocalUser", userRb, StringComparison.Ordinal);

        string memRb = Decode(Single(steps, "UMem_svc_Ops").RollbackCommand!);
        Assert.Contains("Get-ItemProperty -Path $__mkey", memRb, StringComparison.Ordinal);
        Assert.Contains("Remove-LocalGroupMember", memRb, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovalScripts_LogFailures_RatherThanSwallowSilently()
    {
        var group = new GroupModel { Name = "Ops", RemoveOnUninstall = true };

        var step = Single(UtilUserGroupCommandFactory.BuildSteps([group], []), "UGrpD_Ops");
        Assert.NotNull(step.UninstallCommand);
        Assert.Contains("Error.WriteLine", Decode(step.UninstallCommand!), StringComparison.Ordinal);
    }

    [Fact]
    public void DirectlyConstructedModel_WithInvalidName_ThrowsLoud()
    {
        // Defense in depth: a model built directly (bypassing the builder validators) with an injection name
        // must not silently reach a SYSTEM-context script.
        var group = new GroupModel { Name = "bad;name" };

        Assert.Throws<InvalidOperationException>(() => UtilUserGroupCommandFactory.BuildSteps([group], []));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ExecutionStep Single(IReadOnlyList<ExecutionStep> steps, string id)
    {
        var step = steps.SingleOrDefault(s => s.Id == id);
        Assert.NotNull(step);
        return step!;
    }

    private static string Decode(string installCommand)
    {
        const string marker = "-EncodedCommand ";
        int idx = installCommand.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"not an -EncodedCommand invocation: {installCommand}");
        int end = installCommand.IndexOf(" \"", idx, StringComparison.Ordinal);
        string b64 = (end >= 0 ? installCommand[(idx + marker.Length)..end] : installCommand[(idx + marker.Length)..]).Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(b64));
    }
}
