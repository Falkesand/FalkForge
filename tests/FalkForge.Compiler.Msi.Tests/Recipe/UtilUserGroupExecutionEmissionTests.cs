using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using FalkForge.Extensions.Util;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Proves the Util extension's User/Group management reaches the COMPILED MSI as genuinely-scheduled
/// deferred, elevated (SYSTEM) custom actions — create local group, create local user, add group
/// membership, plus rollback and reverse-ordered uninstall removals — and that a user password rides the
/// secure credential channel (type-51 <c>SetProperty</c> carrying a property token / SecureString
/// conversion, never a plaintext password), mirroring <see cref="SqlExecutionEmissionTests"/>. Before this
/// branch, <see cref="UtilExtension"/> had no sink for User/Group at all: the models were built and dropped.
/// This is the most security-sensitive Util feature — it creates local accounts as SYSTEM with secrets.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UtilUserGroupExecutionEmissionTests
{
    private const int InScript = CustomActionType.InScript;
    private const int NoImpersonate = CustomActionType.NoImpersonate;
    private const int Rollback = CustomActionType.Rollback;
    private const int SetProperty = CustomActionType.SetProperty;

    [Fact]
    public void GroupUserMembership_ProduceDeferredSystemActions_InCreateOrder()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        Assert.True(util.AddGroup(g => g.Name("FalkForgeOps")).IsSuccess);
        Assert.True(util.AddUser(u => u
            .Name("svcFalk").PasswordProperty("USERPASSWORD").MemberOf("FalkForgeOps")).IsSuccess);

        using var db = Compile(scratch, "UtilUgCreateApp", util);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // Group create: deferred + SYSTEM, runs New-LocalGroup.
        var group = actions["UGrp_FalkForgeOps"];
        Assert.True((group.Type & InScript) != 0 && (group.Type & NoImpersonate) != 0,
            "group create action must be deferred + SYSTEM");
        Assert.Contains("New-LocalGroup", DecodeEncoded(group.Target), StringComparison.Ordinal);

        // User create: deferred + SYSTEM, runs New-LocalUser and converts the password to a SecureString.
        var user = actions["UUsr_svcFalk"];
        Assert.True((user.Type & InScript) != 0 && (user.Type & NoImpersonate) != 0,
            "user create action must be deferred + SYSTEM");
        string userScript = DecodeEncoded(user.Target);
        Assert.Contains("New-LocalUser", userScript, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-SecureString", userScript, StringComparison.Ordinal);

        // Membership: deferred + SYSTEM, runs Add-LocalGroupMember.
        var member = actions["UMem_svcFalk_FalkForgeOps"];
        Assert.True((member.Type & InScript) != 0 && (member.Type & NoImpersonate) != 0);
        Assert.Contains("Add-LocalGroupMember", DecodeEncoded(member.Target), StringComparison.Ordinal);

        // Install order: group BEFORE user BEFORE membership, all between InstallInitialize/InstallFinalize.
        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.True(sequence["UGrp_FalkForgeOps"] < sequence["UUsr_svcFalk"], "group must be created before user");
        Assert.True(sequence["UUsr_svcFalk"] < sequence["UMem_svcFalk_FalkForgeOps"], "user must exist before membership");
        Assert.InRange(sequence["UGrp_FalkForgeOps"], init + 1, finalize - 1);
        Assert.InRange(sequence["UMem_svcFalk_FalkForgeOps"], init + 1, finalize - 1);
    }

    [Fact]
    public void UserPassword_EmitsSecurePropertyChannel_WithNoPlaintextPassword()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        Assert.True(util.AddUser(u => u.Name("svcFalk").PasswordProperty("USERPASSWORD")).IsSuccess);

        using var db = Compile(scratch, "UtilUgSecureApp", util);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // Type-51 SetProperty carries the property TOKEN into the deferred action's CustomActionData.
        var setProp = actions["UUsr_svcFalk_d"];
        Assert.Equal(SetProperty, setProp.Type);
        Assert.Equal("UUsr_svcFalk", setProp.Source);
        Assert.Equal("[USERPASSWORD]", setProp.Target);
        Assert.True(sequence["UUsr_svcFalk_d"] < sequence["UUsr_svcFalk"]);

        // The deferred user-create action converts the password to a SecureString and reads it from the
        // secure CustomActionData channel — the secret is never baked into the script body.
        string userScript = DecodeEncoded(actions["UUsr_svcFalk"].Target);
        Assert.Contains("ConvertTo-SecureString", userScript, StringComparison.Ordinal);
        Assert.Contains("[CustomActionData]", actions["UUsr_svcFalk"].Target, StringComparison.Ordinal);

        // The only place the secret is referenced anywhere in the MSI is the runtime property token in the
        // type-51 SetProperty target — never a resolved plaintext value baked into any CustomAction.
        foreach (var (action, value) in AllCustomActionTargets(db))
        {
            if (action == "UUsr_svcFalk_d")
                continue; // the SetProperty target legitimately holds the [USERPASSWORD] token
            Assert.DoesNotContain("USERPASSWORD", value, StringComparison.Ordinal);
        }

        // The secret-carrying properties (source + deferred action) are listed in MsiHiddenProperties so a
        // verbose msiexec /L*v install does not log the resolved password.
        var hidden = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property`='MsiHiddenProperties'", 1);
        Assert.True(hidden.IsSuccess, hidden.IsFailure ? hidden.Error.Message : "");
        string hiddenList = Assert.Single(hidden.Value)[0] ?? "";
        Assert.Contains("USERPASSWORD", hiddenList, StringComparison.Ordinal);
        Assert.Contains("UUsr_svcFalk", hiddenList, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPassword_UpdateIfExists_EmitsNoMsiHiddenPropertiesRow()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        Assert.True(util.AddUser(u => u.Name("svcFalk").UpdateIfExists()).IsSuccess);

        using var db = Compile(scratch, "UtilUgNoSecretApp", util);

        var hidden = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property`='MsiHiddenProperties'", 1);
        Assert.True(hidden.IsSuccess, hidden.IsFailure ? hidden.Error.Message : "");
        Assert.Empty(hidden.Value); // no credential carried → no row
    }

    [Fact]
    public void UninstallRemovals_AreSequencedInReverse_MembershipThenUserThenGroup()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        Assert.True(util.AddGroup(g => g.Name("FalkForgeOps").RemoveOnUninstall()).IsSuccess);
        Assert.True(util.AddUser(u => u
            .Name("svcFalk").PasswordProperty("USERPASSWORD").MemberOf("FalkForgeOps").RemoveOnUninstall()).IsSuccess);

        using var db = Compile(scratch, "UtilUgRemoveApp", util);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // All three removals are deferred + SYSTEM and run the correct cmdlets.
        Assert.Contains("Remove-LocalGroupMember", DecodeEncoded(actions["UMemD_svcFalk_FalkForgeOps_un"].Target), StringComparison.Ordinal);
        Assert.Contains("Remove-LocalUser", DecodeEncoded(actions["UUsrD_svcFalk_un"].Target), StringComparison.Ordinal);
        Assert.Contains("Remove-LocalGroup", DecodeEncoded(actions["UGrpD_FalkForgeOps_un"].Target), StringComparison.Ordinal);

        // Reverse of creation: membership removed BEFORE user removed BEFORE group removed.
        int membership = sequence["UMemD_svcFalk_FalkForgeOps_un"];
        int userDel = sequence["UUsrD_svcFalk_un"];
        int groupDel = sequence["UGrpD_FalkForgeOps_un"];
        Assert.True(membership < userDel, "membership must be removed before the user");
        Assert.True(userDel < groupDel, "user must be removed before the group");

        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.InRange(membership, init + 1, finalize - 1);
        Assert.InRange(groupDel, init + 1, finalize - 1);
    }

    [Fact]
    public void UserCreate_HasRollbackThatRemovesTheUser()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        Assert.True(util.AddUser(u => u.Name("svcFalk").PasswordProperty("USERPASSWORD")).IsSuccess);

        using var db = Compile(scratch, "UtilUgRollbackApp", util);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        var rb = actions["UUsr_svcFalk_rb"];
        Assert.True((rb.Type & Rollback) != 0, "user create must have a rollback action");
        Assert.Contains("Remove-LocalUser", DecodeEncoded(rb.Target), StringComparison.Ordinal);
        Assert.True(sequence["UUsr_svcFalk_rb"] < sequence["UUsr_svcFalk"], "rollback scheduled before the create");
    }

    [Fact]
    public void LiteralPassword_IsEmbedded_ProvingTheSecurePathDiffers()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        // Literal password path (USR010-warned) — embeds the value; the contrast with the secure test proves
        // the secure path genuinely keeps the secret out of the MSI.
        Assert.True(util.AddUser(u => u.Name("svcFalk").Password("PlAiNtExT123")).IsSuccess);

        using var db = Compile(scratch, "UtilUgLiteralApp", util);
        var setProp = QueryCustomActions(db)["UUsr_svcFalk_d"];

        Assert.Equal("PlAiNtExT123", setProp.Target);
    }

    /// <summary>
    /// Proves the User/Group feature genuinely creates a local group, a local user, and a membership on a
    /// real per-machine install, and removes them on uninstall — not merely authored into the MSI. Gated
    /// behind <c>FALKFORGE_E2E=1</c> AND <c>FALKFORGE_REAL_SYSTEM_E2E=1</c> AND administrator elevation.
    /// Honestly skips (never a silent fake pass) when a gate is closed.
    /// </summary>
    [Fact]
    public void UserGroupMembership_AreCreatedThenRemoved_OnRealInstall()
    {
        if (Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real User/Group install e2e is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (Environment.GetEnvironmentVariable("FALKFORGE_REAL_SYSTEM_E2E") != "1")
            Assert.Skip("Real User/Group install mutates machine-wide state: set FALKFORGE_REAL_SYSTEM_E2E=1 " +
                        "on a machine you own to run it.");
        if (!IsElevated())
            Assert.Skip("Real User/Group install requires administrator elevation; run the test host elevated.");

        string suffix = Guid.NewGuid().ToString("N")[..8];
        string userName = "ffE2eU" + suffix;
        string groupName = "ffE2eG" + suffix;

        using var scratch = new Scratch();
        var util = new UtilExtension();
        Assert.True(util.AddGroup(g => g.Name(groupName).RemoveOnUninstall()).IsSuccess);
        Assert.True(util.AddUser(u => u
            .Name(userName).Password("P@ssw0rd!" + suffix).MemberOf(groupName).RemoveOnUninstall()).IsSuccess);

        var package = MinimalPackage(scratch, "UtilUgE2eApp");
        var result = new MsiCompiler(new WindowsFileSystem()).Use(util).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        string msi = result.Value;

        RemoveLocalUserIfPresent(userName);
        RemoveLocalGroupIfPresent(groupName);
        try
        {
            int install = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.True(install is 0 or 3010, $"msiexec install exit code {install}");
            Assert.True(LocalUserExists(userName), "local user was not created by the deferred action");
            Assert.True(LocalGroupExists(groupName), "local group was not created by the deferred action");
            Assert.True(IsGroupMember(groupName, userName), "user was not added to the group");

            int uninstall = RunMsiExec($"/x \"{msi}\" /qn /norestart");
            Assert.True(uninstall is 0 or 3010, $"msiexec uninstall exit code {uninstall}");
            Assert.False(LocalUserExists(userName), "local user was not removed on uninstall");
            Assert.False(LocalGroupExists(groupName), "local group was not removed on uninstall");
        }
        finally
        {
            RemoveLocalUserIfPresent(userName);
            RemoveLocalGroupIfPresent(groupName);
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static MsiDatabase Compile(Scratch scratch, string name, UtilExtension util)
    {
        var package = MinimalPackage(scratch, name);
        var compiler = new MsiCompiler(new WindowsFileSystem());
        var result = compiler.Use(util).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
    }

    private static PackageModel MinimalPackage(Scratch scratch, string name)
    {
        var sourceFile = Path.Combine(scratch.SourceDir, "app.exe");
        File.WriteAllText(sourceFile, "payload for util user/group execution emission test");

        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / name));
        });
    }

    private static Dictionary<string, (int Type, string Source, string Target)> QueryCustomActions(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`", 4);
        Assert.True(rows.IsSuccess, $"CustomAction query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

        var map = new Dictionary<string, (int, string, string)>(StringComparer.Ordinal);
        foreach (var r in rows.Value)
        {
            int type = int.Parse(r[1] ?? "0", CultureInfo.InvariantCulture);
            map[r[0] ?? ""] = (type, r[2] ?? "", r[3] ?? "");
        }

        return map;
    }

    private static IEnumerable<(string Action, string Target)> AllCustomActionTargets(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Action`, `Target` FROM `CustomAction`", 2);
        Assert.True(rows.IsSuccess);
        foreach (var r in rows.Value)
            yield return (r[0] ?? "", r[1] ?? "");
    }

    private static Dictionary<string, int> QuerySequence(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`", 2);
        Assert.True(rows.IsSuccess, $"InstallExecuteSequence query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows.Value)
            map[r[0] ?? ""] = int.Parse(r[1] ?? "0", CultureInfo.InvariantCulture);

        return map;
    }

    private static string DecodeEncoded(string target)
    {
        const string marker = "-EncodedCommand ";
        int idx = target.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Target is not an -EncodedCommand invocation: {target}");
        int end = target.IndexOf(" \"", idx, StringComparison.Ordinal);
        string base64 = (end >= 0 ? target[(idx + marker.Length)..end] : target[(idx + marker.Length)..]).Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(base64));
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int RunMsiExec(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("msiexec.exe", arguments) { UseShellExecute = false })!;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static bool LocalUserExists(string name)
        => RunPowerShellSucceeds($"if (Get-LocalUser -Name '{name}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}");

    private static bool LocalGroupExists(string name)
        => RunPowerShellSucceeds($"if (Get-LocalGroup -Name '{name}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}");

    private static bool IsGroupMember(string group, string user)
        => RunPowerShellSucceeds(
            $"if (Get-LocalGroupMember -Group '{group}' -Member '{user}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}");

    private static void RemoveLocalUserIfPresent(string name)
        => RunPowerShellSucceeds($"Remove-LocalUser -Name '{name}' -ErrorAction SilentlyContinue; exit 0");

    private static void RemoveLocalGroupIfPresent(string name)
        => RunPowerShellSucceeds($"Remove-LocalGroup -Name '{name}' -ErrorAction SilentlyContinue; exit 0");

    private static bool RunPowerShellSucceeds(string command)
    {
        using var process = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"")
        {
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"UtilUgExecEmit_{Guid.NewGuid():N}");

        public Scratch()
        {
            SourceDir = Path.Combine(_root, "source");
            OutputDir = Path.Combine(_root, "output");
            Directory.CreateDirectory(SourceDir);
            Directory.CreateDirectory(OutputDir);
        }

        public string SourceDir { get; }
        public string OutputDir { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
