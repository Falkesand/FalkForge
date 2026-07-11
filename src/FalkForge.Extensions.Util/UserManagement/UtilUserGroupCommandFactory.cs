using System.Text;
using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.UserManagement;

/// <summary>
/// Turns <see cref="GroupModel"/>/<see cref="UserModel"/> definitions into <see cref="ExecutionStep"/>
/// declarations — the install/rollback/uninstall commands the MSI compiler schedules as deferred,
/// elevated (SYSTEM) custom actions so local groups, local users and group memberships are genuinely
/// created on the target machine and removed on uninstall, instead of the User/Group models being built
/// and dropped. This is the most security-sensitive Util feature: it creates local accounts as SYSTEM
/// with secret passwords.
///
/// <para><b>Execution vehicle.</b> Every step runs Windows PowerShell (invoked by its fully-qualified
/// <c>[SystemFolder]</c> path, transported base64 via <c>-EncodedCommand</c>) which drives the in-box
/// <c>Microsoft.PowerShell.LocalAccounts</c> cmdlets (<c>New-LocalGroup</c>, <c>New-LocalUser</c>,
/// <c>Add-LocalGroupMember</c>, …) plus the WinNT ADSI provider for the account flags those cmdlets do not
/// expose. No external module is required on the target.</para>
///
/// <para><b>Credentials.</b> A user's password reaches the <b>install</b> action only through the seam's
/// <see cref="ExecutionStep.CustomActionData"/> channel: an immediate type-51 <c>SetProperty</c> copies
/// the referenced secure MSI property (populated at run time via <c>SetSecureProperty</c>) into the
/// deferred action, read here as <c>$args[0]</c> and immediately converted to a <c>SecureString</c> with
/// <c>ConvertTo-SecureString</c>. The password is therefore never stored in the MSI, never echoed and
/// never logged (the carrying properties are listed in <c>MsiHiddenProperties</c> by
/// <see cref="UtilHiddenPropertiesContributor"/>). A <i>literal</i> password (discouraged, USR010-warned)
/// is embedded in the SetProperty target instead — the contrast proves the secure path keeps the secret
/// out of the MSI.</para>
///
/// <para><b>Injection safety.</b> Every author-supplied name/description is single-quoted via
/// <see cref="CommandLine.PowerShellSingleQuote"/> before it reaches a command that runs as SYSTEM — a
/// malicious group or user name cannot break out of its literal. Names are additionally validated to the
/// Windows account-name grammar at author time (USR003/GRP002).</para>
///
/// <para><b>Scope / deferrals.</b> Only accounts with no <see cref="UserModel.Domain"/>/
/// <see cref="GroupModel.Domain"/> are created (the local-account cmdlets cannot create domain
/// principals); a domain-qualified user is referenced for group membership only (<c>Domain\Name</c>), and
/// a domain-qualified group produces no steps. Ordering is deterministic: on install, groups are created
/// first, then users, then memberships; on uninstall the removals run in reverse (memberships, then users,
/// then groups).</para>
/// </summary>
internal static class UtilUserGroupCommandFactory
{
    internal static IReadOnlyList<ExecutionStep> BuildSteps(
        IReadOnlyList<GroupModel> groups, IReadOnlyList<UserModel> users)
        => BuildPlan(groups, users).Steps;

    /// <summary>
    /// The names of every MSI property that carries a user password at run time — the secure source
    /// property (<see cref="UserModel.PasswordProperty"/>) plus each deferred user-create action's
    /// CustomActionData property (named after the action). Listed in <c>MsiHiddenProperties</c> so their
    /// values are scrubbed from a verbose MSI log.
    /// </summary>
    internal static IReadOnlyList<string> CollectHiddenPropertyNames(IReadOnlyList<UserModel> users)
        => BuildPlan([], users).HiddenPropertyNames;

    private static UserGroupPlan BuildPlan(IReadOnlyList<GroupModel> groups, IReadOnlyList<UserModel> users)
    {
        // Defense in depth: UserModel/GroupModel are public and can be constructed directly (bypassing the
        // builder's USR003/GRP002 validation). Every name reaches a SYSTEM-context command line (and the
        // WinNT ADSI moniker), so re-assert the account-name grammar here and fail loud rather than emit a
        // step for an unvalidated name. Unreachable via the public AddUser/AddGroup sinks, which validate.
        Guard(groups, users);

        var steps = new List<ExecutionStep>();
        var hidden = new HashSet<string>(StringComparer.Ordinal);

        // ── (1) create local groups first so memberships can target them.
        foreach (GroupModel g in groups)
        {
            if (IsLocal(g.Domain) && !string.IsNullOrWhiteSpace(g.Name))
                steps.Add(BuildGroupCreateStep(g));
        }

        // ── (2) create/update local users.
        foreach (UserModel u in users)
        {
            if (IsLocal(u.Domain) && !string.IsNullOrWhiteSpace(u.Name))
            {
                ExecutionStep step = BuildUserCreateStep(u);
                steps.Add(step);
                RecordSecret(hidden, step, u);
            }
        }

        // ── (3) add users to groups (works for both local and domain-qualified users).
        foreach (UserModel u in users)
        {
            if (string.IsNullOrWhiteSpace(u.Name))
                continue;
            foreach (string group in u.Groups)
            {
                if (!string.IsNullOrWhiteSpace(group))
                    steps.Add(BuildMembershipAddStep(u, group));
            }
        }

        // ── (4) uninstall removals, in REVERSE of the install order: memberships, then users, then groups.
        //     Each removal is an uninstall-only step (install gated off) so the emitter schedules the
        //     removal commands in the removal band in this list order — the reverse of creation.
        foreach (UserModel u in users)
        {
            if (string.IsNullOrWhiteSpace(u.Name))
                continue;
            foreach (string group in u.Groups)
            {
                if (!string.IsNullOrWhiteSpace(group))
                    steps.Add(BuildMembershipRemoveStep(u, group));
            }
        }

        foreach (UserModel u in users)
        {
            if (u.RemoveOnUninstall && IsLocal(u.Domain) && !string.IsNullOrWhiteSpace(u.Name))
                steps.Add(BuildUserRemoveStep(u));
        }

        foreach (GroupModel g in groups)
        {
            if (g.RemoveOnUninstall && IsLocal(g.Domain) && !string.IsNullOrWhiteSpace(g.Name))
                steps.Add(BuildGroupRemoveStep(g));
        }

        return new UserGroupPlan(steps, hidden.OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Re-asserts the Windows account-name grammar on every group name, user name and referenced group so a
    /// directly-constructed model (which bypasses the builder validators) can never smuggle an ADSI-moniker
    /// or command-line metacharacter into a SYSTEM-context script. Throws loudly; never silently drops.
    /// </summary>
    private static void Guard(IReadOnlyList<GroupModel> groups, IReadOnlyList<UserModel> users)
    {
        foreach (GroupModel g in groups)
            Require(g.Name, "group");

        foreach (UserModel u in users)
        {
            Require(u.Name, "user");
            foreach (string group in u.Groups)
                Require(group, "group membership");
        }
    }

    private static void Require(string name, string kind)
    {
        if (!UserValidator.IsValidAccountName(name))
        {
            throw new InvalidOperationException(
                $"Invalid {kind} name '{name}': not a valid Windows account name. " +
                "Names must be supplied through UtilExtension.AddUser/AddGroup, which validate them.");
        }
    }

    private static void RecordSecret(HashSet<string> hidden, ExecutionStep step, UserModel u)
    {
        if (string.IsNullOrEmpty(u.PasswordProperty) && string.IsNullOrEmpty(u.Password))
            return; // no credential → CustomActionData (if any) carries no secret.

        hidden.Add(step.Id); // the deferred action's CustomActionData property holds the resolved password.
        if (!string.IsNullOrEmpty(u.PasswordProperty))
            hidden.Add(u.PasswordProperty!); // the secure source property populated via SetSecureProperty.
    }

    // ── group create / remove ───────────────────────────────────────────────

    private static ExecutionStep BuildGroupCreateStep(GroupModel g)
    {
        string createScript = BuildGroupCreateScript(g);
        // Rollback removes the group ONLY when !UpdateIfExists. In that mode the create script throws if the
        // group already exists (it never adopts a pre-existing group), so reaching the created state proves
        // we created it — deleting it on rollback is safe. In UpdateIfExists mode the group may have
        // pre-existed, so we deliberately do NOT delete on rollback: a freshly-created group orphaned by a
        // failed install is an acceptable trade-off against destroying a group that was already present.
        string? rollback = g.UpdateIfExists ? null : UtilPowerShellEncoder.Encode(BuildGroupRemoveScript(g));

        return new ExecutionStep
        {
            Id = UtilStepId.Make("UGrp_", g.Name),
            InstallCommand = UtilPowerShellEncoder.Encode(createScript),
            RollbackCommand = rollback,
        };
    }

    private static ExecutionStep BuildGroupRemoveStep(GroupModel g)
        => new()
        {
            Id = UtilStepId.Make("UGrpD_", g.Name),
            InstallCommand = UtilPowerShellEncoder.Encode("exit 0"),
            InstallCondition = "0",
            UninstallCommand = UtilPowerShellEncoder.Encode(BuildGroupRemoveScript(g)),
        };

    // ── user create / remove ────────────────────────────────────────────────

    private static ExecutionStep BuildUserCreateStep(UserModel u)
    {
        string createScript = BuildUserCreateScript(u);
        string? customActionData = InstallPasswordChannel(u);
        // Rollback removes the user ONLY when !UpdateIfExists. In that mode the create script throws if the
        // user already exists (never adopts one), so reaching the created state proves we created it. In
        // UpdateIfExists mode the account may have pre-existed, so we deliberately do NOT delete on rollback
        // (same trade-off as the group step): orphaning a freshly-created account beats deleting one that
        // was already present.
        string? rollback = u.UpdateIfExists ? null : UtilPowerShellEncoder.Encode(BuildUserRemoveScript(u));

        return new ExecutionStep
        {
            Id = UtilStepId.Make("UUsr_", u.Name),
            // The resolved password reaches $args[0] as the double-quoted trailing argument. Like the SQL
            // extension (a documented limitation of the EXE-custom-action transport), the value MUST be
            // quote-free: a literal password is rejected at author time if it contains a double quote
            // (USR012), and a PasswordProperty value supplied via SetSecureProperty is likewise expected to
            // be quote-free. Because the value lands in $args (not re-parsed as PowerShell switches), a
            // quote-free password cannot inject; a value with a quote would merely be truncated/mangled, so
            // it is disallowed rather than silently corrupting a privileged account's credential.
            InstallCommand = customActionData is null
                ? UtilPowerShellEncoder.Encode(createScript)
                : UtilPowerShellEncoder.EncodeWithTrailingArgument(createScript, "[CustomActionData]"),
            CustomActionData = customActionData,
            RollbackCommand = rollback,
        };
    }

    private static ExecutionStep BuildUserRemoveStep(UserModel u)
        => new()
        {
            Id = UtilStepId.Make("UUsrD_", u.Name),
            InstallCommand = UtilPowerShellEncoder.Encode("exit 0"),
            InstallCondition = "0",
            UninstallCommand = UtilPowerShellEncoder.Encode(BuildUserRemoveScript(u)),
        };

    // ── membership add / remove ─────────────────────────────────────────────

    private static ExecutionStep BuildMembershipAddStep(UserModel u, string group)
        => new()
        {
            Id = UtilStepId.Make("UMem_", u.Name + "_" + group),
            InstallCommand = UtilPowerShellEncoder.Encode(BuildMembershipAddScript(u, group)),
            RollbackCommand = UtilPowerShellEncoder.Encode(BuildMembershipRemoveScript(u, group)),
        };

    private static ExecutionStep BuildMembershipRemoveStep(UserModel u, string group)
        => new()
        {
            Id = UtilStepId.Make("UMemD_", u.Name + "_" + group),
            InstallCommand = UtilPowerShellEncoder.Encode("exit 0"),
            InstallCondition = "0",
            UninstallCommand = UtilPowerShellEncoder.Encode(BuildMembershipRemoveScript(u, group)),
        };

    // ── PowerShell script generation ────────────────────────────────────────

    private static string BuildGroupCreateScript(GroupModel g)
    {
        string descArg = string.IsNullOrEmpty(g.Description)
            ? string.Empty
            : " -Description " + CommandLine.PowerShellSingleQuote(g.Description!);

        var sb = new StringBuilder(320);
        sb.Append("$ErrorActionPreference = 'Stop'\n");
        sb.Append("try {\n");
        sb.Append("  $__g = ").Append(CommandLine.PowerShellSingleQuote(g.Name)).Append('\n');
        sb.Append("  $__existing = Get-LocalGroup -Name $__g -ErrorAction SilentlyContinue\n");
        sb.Append("  if ($null -ne $__existing) {\n");
        if (g.UpdateIfExists)
        {
            // Adopt the pre-existing group (optionally refreshing its description). No rollback removal is
            // emitted for this mode, so adoption never leads to deleting a group we did not create.
            if (!string.IsNullOrEmpty(g.Description))
                sb.Append("    Set-LocalGroup -Name $__g").Append(descArg).Append('\n');
            else
                sb.Append("    # group already present; nothing to change\n");
        }
        else
        {
            // Fail loud rather than silently adopt: this guarantees the paired rollback (which removes the
            // group) only ever targets a group THIS install created.
            sb.Append("    throw \"Local group '$__g' already exists; enable UpdateIfExists to adopt it.\"\n");
        }

        sb.Append("  } else {\n");
        sb.Append("    New-LocalGroup -Name $__g").Append(descArg).Append(" | Out-Null\n");
        sb.Append("  }\n");
        sb.Append("  exit 0\n");
        sb.Append("} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }\n");
        return sb.ToString();
    }

    private static string BuildGroupRemoveScript(GroupModel g)
    {
        var sb = new StringBuilder(200);
        sb.Append("try { Remove-LocalGroup -Name ")
          .Append(CommandLine.PowerShellSingleQuote(g.Name))
          .Append(" -ErrorAction SilentlyContinue } catch { [Console]::Error.WriteLine($_.Exception.Message) }\n");
        sb.Append("exit 0\n");
        return sb.ToString();
    }

    private static string BuildUserCreateScript(UserModel u)
    {
        var sb = new StringBuilder(1024);
        sb.Append("$ErrorActionPreference = 'Stop'\n");
        sb.Append("try {\n");
        // The password rides $args[0] (the secure CustomActionData channel) and is converted to a
        // SecureString immediately; the plaintext form is never assigned to a durable variable.
        sb.Append("  $__pw = if ($args.Count -ge 1) { $args[0] } else { '' }\n");
        sb.Append("  $__sec = if (-not [string]::IsNullOrEmpty($__pw)) { ConvertTo-SecureString $__pw -AsPlainText -Force } else { $null }\n");
        sb.Append("  $__u = ").Append(CommandLine.PowerShellSingleQuote(u.Name)).Append('\n');
        string descArg = string.IsNullOrEmpty(u.Description)
            ? string.Empty
            : " -Description " + CommandLine.PowerShellSingleQuote(u.Description!);

        sb.Append("  $__existing = Get-LocalUser -Name $__u -ErrorAction SilentlyContinue\n");
        sb.Append("  if ($null -ne $__existing) {\n");
        if (u.UpdateIfExists)
        {
            sb.Append("    if ($null -ne $__sec) { Set-LocalUser -Name $__u -Password $__sec }\n");
            if (!string.IsNullOrEmpty(u.Description))
                sb.Append("    Set-LocalUser -Name $__u").Append(descArg).Append('\n');
        }
        else
        {
            sb.Append("    throw \"Local user '$__u' already exists; enable UpdateIfExists to modify it.\"\n");
        }

        sb.Append("  } else {\n");
        // Creating a NEW local account: a password is mandatory. If none reached us (secure property unset
        // at run time, or an UpdateIfExists user that turned out to be absent), fail loud — never silently
        // create a passwordless local account, which would be a standing credential-free logon target.
        sb.Append("    if ($null -eq $__sec) { throw \"Cannot create local user '$__u' without a password. ")
          .Append("Supply a PasswordProperty (SetSecureProperty) or a literal password.\" }\n");
        sb.Append("    New-LocalUser -Name $__u -Password $__sec").Append(descArg).Append(" | Out-Null\n");
        sb.Append("  }\n");

        // Account flags via the WinNT ADSI provider — uniform for create and update, and covering flags the
        // Set-LocalUser cmdlet does not expose (cannot-change-password, disable, force-expire).
        sb.Append("  $__adsi = [ADSI]('WinNT://./' + $__u + ',user')\n");
        sb.Append("  $__flags = [int]$__adsi.UserFlags.Value\n");
        sb.Append("  if (").Append(Bool(u.PasswordNeverExpires))
          .Append(") { $__flags = $__flags -bor 0x10000 } else { $__flags = $__flags -band (-bnot 0x10000) }\n");
        sb.Append("  if (").Append(Bool(u.CanNotChangePassword))
          .Append(") { $__flags = $__flags -bor 0x40 } else { $__flags = $__flags -band (-bnot 0x40) }\n");
        sb.Append("  if (").Append(Bool(u.Disabled))
          .Append(") { $__flags = $__flags -bor 0x2 } else { $__flags = $__flags -band (-bnot 0x2) }\n");
        sb.Append("  $__adsi.UserFlags = $__flags\n");
        if (u.PasswordExpired)
            sb.Append("  $__adsi.PasswordExpired = 1\n");
        sb.Append("  $__adsi.SetInfo()\n");

        sb.Append("  exit 0\n");
        sb.Append("} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }\n");
        return sb.ToString();
    }

    private static string BuildUserRemoveScript(UserModel u)
    {
        var sb = new StringBuilder(200);
        sb.Append("try { Remove-LocalUser -Name ")
          .Append(CommandLine.PowerShellSingleQuote(u.Name))
          .Append(" -ErrorAction SilentlyContinue } catch { [Console]::Error.WriteLine($_.Exception.Message) }\n");
        sb.Append("exit 0\n");
        return sb.ToString();
    }

    private static string BuildMembershipAddScript(UserModel u, string group)
    {
        string member = MemberName(u);
        var sb = new StringBuilder(320);
        sb.Append("$ErrorActionPreference = 'Stop'\n");
        sb.Append("try {\n");
        sb.Append("  $__g = ").Append(CommandLine.PowerShellSingleQuote(group)).Append('\n');
        sb.Append("  $__m = ").Append(CommandLine.PowerShellSingleQuote(member)).Append('\n');
        sb.Append("  $__has = Get-LocalGroupMember -Group $__g -Member $__m -ErrorAction SilentlyContinue\n");
        sb.Append("  if ($null -eq $__has) { Add-LocalGroupMember -Group $__g -Member $__m }\n");
        sb.Append("  exit 0\n");
        sb.Append("} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }\n");
        return sb.ToString();
    }

    private static string BuildMembershipRemoveScript(UserModel u, string group)
    {
        string member = MemberName(u);
        var sb = new StringBuilder(240);
        sb.Append("try { Remove-LocalGroupMember -Group ")
          .Append(CommandLine.PowerShellSingleQuote(group))
          .Append(" -Member ").Append(CommandLine.PowerShellSingleQuote(member))
          .Append(" -ErrorAction SilentlyContinue } catch { [Console]::Error.WriteLine($_.Exception.Message) }\n");
        sb.Append("exit 0\n");
        return sb.ToString();
    }

    // ── credential channel + helpers ────────────────────────────────────────

    /// <summary>
    /// The install-action CustomActionData for a user-create step: the secure property token, the literal
    /// password (MSI-escaped, embedded plaintext — USR010), or <see langword="null"/> when the user has no
    /// credential (an UpdateIfExists user that keeps its current password).
    /// </summary>
    private static string? InstallPasswordChannel(UserModel u)
    {
        if (!string.IsNullOrEmpty(u.PasswordProperty))
            return string.Concat("[", u.PasswordProperty, "]");
        if (!string.IsNullOrEmpty(u.Password))
            return CommandLine.MsiFormatEscape(u.Password!);
        return null;
    }

    private static string MemberName(UserModel u)
        => IsLocal(u.Domain) ? u.Name : string.Concat(u.Domain, "\\", u.Name);

    private static bool IsLocal(string? domain)
        => string.IsNullOrWhiteSpace(domain)
           || string.Equals(domain, ".", StringComparison.Ordinal)
           || string.Equals(domain, "localhost", StringComparison.OrdinalIgnoreCase)
           || string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    private static string Bool(bool value) => value ? "$true" : "$false";

    private sealed record UserGroupPlan(
        IReadOnlyList<ExecutionStep> Steps,
        IReadOnlyList<string> HiddenPropertyNames);
}
