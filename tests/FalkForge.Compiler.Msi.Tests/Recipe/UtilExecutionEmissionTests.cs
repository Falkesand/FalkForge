using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using FalkForge.Extensions.Util;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Proves the four non-secret Util execution features (QuietExec, RemoveFolderEx, FileShare,
/// InternetShortcut) reach the COMPILED MSI as genuinely-scheduled deferred custom actions — not
/// merely authored table data that nothing runs — mirroring
/// <see cref="ExecutionStepEmissionTests"/>/<c>FirewallRule_ProducesLiveCreateRollbackAndRemoveActions</c>.
/// Before this branch, <see cref="UtilExtension"/> had no sink for these builders at all: the models
/// were built and dropped.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UtilExecutionEmissionTests
{
    private const int InScript = CustomActionType.InScript;
    private const int NoImpersonate = CustomActionType.NoImpersonate;
    private const int SetProperty = CustomActionType.SetProperty;

    [Fact]
    public void QuietExec_ProducesDeferredInstallActionWithRollback_SequencedInExecuteScript()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        var result = util.AddQuietExec(q => q
            .Id("Provision").Command("setup.exe /quiet").RollbackCommand("undo.exe /quiet"));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var db = Compile(scratch, "UtilQuietExecApp", util);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        var install = actions["Qe_Provision"];
        Assert.True((install.Type & InScript) != 0 && (install.Type & NoImpersonate) != 0,
            "QuietExec install action must be deferred + SYSTEM");
        // QuietExec is emitted with a LIVE command (not base64) so MSI Formatted tokens resolve; the
        // command runs via the fully-qualified [SystemFolder]cmd.exe.
        Assert.StartsWith("[SystemFolder]cmd.exe", install.Target, StringComparison.Ordinal);
        Assert.Contains("setup.exe /quiet", install.Target, StringComparison.Ordinal);

        Assert.True((actions["Qe_Provision_rb"].Type & CustomActionType.Rollback) != 0);
        Assert.Contains("undo.exe /quiet", actions["Qe_Provision_rb"].Target, StringComparison.Ordinal);

        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.InRange(sequence["Qe_Provision"], init + 1, finalize - 1);
        Assert.True(sequence["Qe_Provision_rb"] < sequence["Qe_Provision"]);
    }

    [Fact]
    public void QuietExec_MsiFormattedTokens_SurviveLiveInCompiledTarget()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        var result = util.AddQuietExec(q => q
            .Id("Warmup").Command("\"[INSTALLDIR]app.exe\" --env [ENVIRONMENT]").WorkingDir("[INSTALLDIR]"));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var db = Compile(scratch, "UtilQuietExecTokenApp", util);
        string target = QueryCustomActions(db)["Qe_Warmup"].Target;

        // The regression guard: [INSTALLDIR]/[ENVIRONMENT] must remain literal bracket text in the
        // compiled Target so the installer resolves them at run time — not be buried inside base64.
        Assert.Contains("[INSTALLDIR]", target, StringComparison.Ordinal);
        Assert.Contains("[ENVIRONMENT]", target, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveFolderEx_OnInstall_WithProperty_EmitsSetPropertyThenDeferredAction()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        var result = util.AddRemoveFolderEx(r => r.Id("Stale").Property("STALEFOLDER").OnInstall());
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var db = Compile(scratch, "UtilRfxInstallApp", util);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // The live-token channel: an immediate SetProperty action carries "[STALEFOLDER]" into the
        // deferred action's CustomActionData.
        var setProp = actions["Rfx_Stale_d"];
        Assert.Equal(SetProperty, setProp.Type);
        Assert.Equal("Rfx_Stale", setProp.Source);
        Assert.Equal("[STALEFOLDER]", setProp.Target);
        Assert.True(sequence["Rfx_Stale_d"] < sequence["Rfx_Stale"]);

        var install = actions["Rfx_Stale"];
        Assert.True((install.Type & InScript) != 0 && (install.Type & NoImpersonate) != 0);
        Assert.False(actions.ContainsKey("Rfx_Stale_un"), "Install-only mode must not emit an uninstall action");
    }

    [Fact]
    public void RemoveFolderEx_OnUninstall_WithLiteralDirectory_InstallActionIsGatedOff_UninstallActionRemoves()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        var result = util.AddRemoveFolderEx(r => r
            .Id("Cache").Directory(@"C:\ProgramData\UtilExecTest\Cache").OnUninstall());
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var db = Compile(scratch, "UtilRfxUninstallApp", util);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // Structural placeholder: InstallCommand is required by the emitter even when the model wants
        // uninstall-only removal, so it is scheduled with Condition="0" (the standard MSI "never run"
        // idiom) — the row exists but does not execute on install.
        var installSeqRows = db.QueryRows(
            "SELECT `Action`, `Condition` FROM `InstallExecuteSequence` WHERE `Action`='Rfx_Cache'", 2);
        Assert.True(installSeqRows.IsSuccess);
        Assert.Equal("0", Assert.Single(installSeqRows.Value)[1]);

        var uninstall = actions["Rfx_Cache_un"];
        Assert.True((uninstall.Type & InScript) != 0 && (uninstall.Type & NoImpersonate) != 0);
        Assert.Contains(@"C:\ProgramData\UtilExecTest\Cache", DecodeEncoded(uninstall.Target), StringComparison.Ordinal);

        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.InRange(sequence["Rfx_Cache_un"], init + 1, finalize - 1);
    }

    [Fact]
    public void FileShare_ProducesLiveCreateRollbackAndRemoveActions()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        var result = util.AddFileShare(f => f
            .Id("Data").Name("UtilExecTestShare").Directory(@"C:\ProgramData\UtilExecTest\Share")
            .GrantRead("Everyone"));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var db = Compile(scratch, "UtilFileShareApp", util);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        var install = actions["Fsh_Data"];
        Assert.Contains("New-SmbShare", DecodeEncoded(install.Target), StringComparison.Ordinal);
        Assert.True((install.Type & InScript) != 0 && (install.Type & NoImpersonate) != 0);
        // The shared path rides a live trailing argument (outside base64) so a token would resolve.
        Assert.EndsWith("\"C:\\ProgramData\\UtilExecTest\\Share\"", install.Target, StringComparison.Ordinal);

        Assert.Contains("Remove-SmbShare", DecodeEncoded(actions["Fsh_Data_rb"].Target), StringComparison.Ordinal);
        Assert.Contains("Remove-SmbShare", DecodeEncoded(actions["Fsh_Data_un"].Target), StringComparison.Ordinal);

        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.InRange(sequence["Fsh_Data"], init + 1, finalize - 1);
    }

    [Fact]
    public void InternetShortcut_ProducesLiveCreateRollbackAndRemoveActions()
    {
        using var scratch = new Scratch();

        var util = new UtilExtension();
        var result = util.AddInternetShortcut(s => s
            .Id("Home").Name("UtilExecTest Home").Target("https://example.com").Directory(@"C:\ProgramData\UtilExecTest"));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        using var db = Compile(scratch, "UtilInternetShortcutApp", util);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        var install = actions["Isc_Home"];
        Assert.Contains("Set-Content", DecodeEncoded(install.Target), StringComparison.Ordinal);
        Assert.True((install.Type & InScript) != 0 && (install.Type & NoImpersonate) != 0);
        // The target directory rides a live trailing argument (outside base64) so a token would resolve;
        // all three actions target the same directory so uninstall removes what install created.
        Assert.EndsWith("\"C:\\ProgramData\\UtilExecTest\"", install.Target, StringComparison.Ordinal);
        Assert.EndsWith("\"C:\\ProgramData\\UtilExecTest\"", actions["Isc_Home_un"].Target, StringComparison.Ordinal);

        Assert.Contains("Remove-Item", DecodeEncoded(actions["Isc_Home_rb"].Target), StringComparison.Ordinal);
        Assert.Contains("Remove-Item", DecodeEncoded(actions["Isc_Home_un"].Target), StringComparison.Ordinal);

        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.InRange(sequence["Isc_Home"], init + 1, finalize - 1);
    }

    /// <summary>
    /// Proves FileShare is genuinely created on install and removed on uninstall — not merely authored
    /// into the MSI. Gated behind <c>FALKFORGE_E2E=1</c> AND <c>FALKFORGE_REAL_SYSTEM_E2E=1</c> AND
    /// administrator elevation because it runs a real per-machine msiexec install that creates an SMB
    /// share. Honestly skips (never a silent fake pass) when a gate is closed.
    /// </summary>
    [Fact]
    public void FileShare_IsCreatedThenRemoved_OnRealInstall()
    {
        if (Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real FileShare install e2e is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (Environment.GetEnvironmentVariable("FALKFORGE_REAL_SYSTEM_E2E") != "1")
            Assert.Skip("Real FileShare install mutates machine-wide state: set FALKFORGE_REAL_SYSTEM_E2E=1 " +
                        "on a machine you own to run it.");
        if (!IsElevated())
            Assert.Skip("Real FileShare install requires administrator elevation; run the test host elevated.");

        using var scratch = new Scratch();
        string shareDir = Path.Combine(scratch.SourceDir, "ShareRoot");
        Directory.CreateDirectory(shareDir);

        const string shareName = "FfE2eShare";
        var util = new UtilExtension();
        var added = util.AddFileShare(f => f.Id("E2eShare").Name(shareName).Directory(shareDir));
        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Message : "");

        var package = MinimalPackage(scratch, "UtilFileShareE2eApp");
        var result = new MsiCompiler(new WindowsFileSystem()).Use(util).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        string msi = result.Value;

        RemoveShareIfPresent(shareName);
        try
        {
            int install = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.True(install is 0 or 3010, $"msiexec install exit code {install}");
            Assert.True(ShareExists(shareName), "SMB share was not created by the deferred action");

            int uninstall = RunMsiExec($"/x \"{msi}\" /qn /norestart");
            Assert.True(uninstall is 0 or 3010, $"msiexec uninstall exit code {uninstall}");
            Assert.False(ShareExists(shareName), "SMB share was not removed on uninstall");
        }
        finally
        {
            RemoveShareIfPresent(shareName);
        }
    }

    /// <summary>
    /// Proves RemoveFolderEx genuinely deletes a real folder on uninstall (literal Directory path) —
    /// gated the same way as <see cref="FileShare_IsCreatedThenRemoved_OnRealInstall"/>.
    /// </summary>
    [Fact]
    public void RemoveFolderEx_DeletesRealFolder_OnUninstall()
    {
        if (Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real RemoveFolderEx uninstall e2e is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (Environment.GetEnvironmentVariable("FALKFORGE_REAL_SYSTEM_E2E") != "1")
            Assert.Skip("Real RemoveFolderEx uninstall mutates machine-wide state: set " +
                        "FALKFORGE_REAL_SYSTEM_E2E=1 on a machine you own to run it.");
        if (!IsElevated())
            Assert.Skip("Real RemoveFolderEx uninstall requires administrator elevation; run the test host elevated.");

        using var scratch = new Scratch();
        string targetDir = Path.Combine(scratch.SourceDir, "ToDelete");
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "leftover.txt"), "e2e");

        var util = new UtilExtension();
        var added = util.AddRemoveFolderEx(r => r.Id("E2eCache").Directory(targetDir).OnUninstall());
        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Message : "");

        var package = MinimalPackage(scratch, "UtilRfxE2eApp");
        var result = new MsiCompiler(new WindowsFileSystem()).Use(util).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        string msi = result.Value;

        try
        {
            int install = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.True(install is 0 or 3010, $"msiexec install exit code {install}");
            Assert.True(Directory.Exists(targetDir), "folder must still exist after install (uninstall-only mode)");

            int uninstall = RunMsiExec($"/x \"{msi}\" /qn /norestart");
            Assert.True(uninstall is 0 or 3010, $"msiexec uninstall exit code {uninstall}");
            Assert.False(Directory.Exists(targetDir), "folder was not removed by the deferred uninstall action");
        }
        finally
        {
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
        File.WriteAllText(sourceFile, "payload for util execution emission test");

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
        return System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(base64));
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int RunMsiExec(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("msiexec.exe", arguments)
        {
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static bool ShareExists(string shareName)
        => RunPowerShellSucceeds($"if (Get-SmbShare -Name '{shareName}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}");

    private static void RemoveShareIfPresent(string shareName)
        => RunPowerShellSucceeds($"Remove-SmbShare -Name '{shareName}' -Force -ErrorAction SilentlyContinue; exit 0");

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
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"UtilExecEmit_{Guid.NewGuid():N}");

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
