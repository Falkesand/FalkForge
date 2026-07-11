using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using FalkForge.Extensibility;
using FalkForge.Extensions.Firewall;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Proves the reusable install-time <b>execution</b> seam end-to-end on the COMPILED MSI: an
/// <see cref="IExecutionContributor"/>'s <see cref="ExecutionStep"/>s must become deferred, elevated
/// custom actions that are actually sequenced in <c>InstallExecuteSequence</c> — not inert table data.
/// This is the exact gap that let extension work (firewall rules, HTTP ACLs) ship dead: the data
/// landed in the MSI but nothing ran it. The structural tests open the MSI and query the tables; the
/// gated <see cref="FirewallRule_IsCreatedThenRemoved_OnRealInstall"/> proves the action runs for real.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ExecutionStepEmissionTests
{
    private const int InScript = 0x100;
    private const int Rollback = 0x200;
    private const int NoImpersonate = 0x800;
    private const int ExeInDir = 34;
    private const int SetProperty = 51;

    [Fact]
    public void ExecutionStep_BecomesDeferredElevatedActionWithRollback_SequencedInExecuteScript()
    {
        using var scratch = new Scratch();

        var step = new ExecutionStep
        {
            Id = "FfProbe",
            InstallCommand = "powershell.exe -NoProfile -Command \"exit 0\"",
            RollbackCommand = "powershell.exe -NoProfile -Command \"exit 0\"",
            UninstallCommand = "powershell.exe -NoProfile -Command \"exit 0\"",
        };

        var result = Compile(scratch, "ExecMechApp", new FakeExecutionExtension("Fake.Exec", step));
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        using var db = Open(result.Value);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // Install action: type-34 ExeInDir, deferred (in-script), no-impersonate (SYSTEM), TARGETDIR source.
        var install = actions["FfProbe"];
        Assert.Equal(ExeInDir, install.Type & 0x3F);
        Assert.True((install.Type & InScript) != 0, "install action must be deferred (InScript)");
        Assert.True((install.Type & NoImpersonate) != 0, "install action must be no-impersonate (SYSTEM)");
        Assert.Equal("TARGETDIR", install.Source);

        // Rollback action carries the Rollback bit.
        var rollback = actions["FfProbe_rb"];
        Assert.True((rollback.Type & Rollback) != 0, "rollback action must carry the Rollback bit");
        Assert.True((rollback.Type & NoImpersonate) != 0);

        // Uninstall action exists and is deferred.
        var uninstall = actions["FfProbe_un"];
        Assert.True((uninstall.Type & InScript) != 0);

        // All three are sequenced strictly between InstallInitialize and InstallFinalize.
        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        foreach (var name in new[] { "FfProbe", "FfProbe_rb", "FfProbe_un" })
        {
            Assert.True(sequence.ContainsKey(name), $"{name} must appear in InstallExecuteSequence");
            int seq = sequence[name];
            Assert.InRange(seq, init + 1, finalize - 1);
        }

        // Rollback must precede its install action so Windows Installer runs it in reverse on failure.
        Assert.True(sequence["FfProbe_rb"] < sequence["FfProbe"],
            "rollback action must be sequenced before the install action it undoes");
    }

    [Fact]
    public void CustomActionData_EmitsImmediateSetPropertyBeforeTheDeferredAction()
    {
        using var scratch = new Scratch();

        // The secret / late-bound channel: CustomActionData references a runtime property.
        var step = new ExecutionStep
        {
            Id = "FfSecret",
            InstallCommand = "powershell.exe -NoProfile -Command \"exit 0\"",
            CustomActionData = "[MY_SECRET]",
        };

        var result = Compile(scratch, "ExecSecretApp", new FakeExecutionExtension("Fake.Secret", step));
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        using var db = Open(result.Value);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // A type-51 SetProperty action whose Source is the deferred action's name (so its value
        // becomes that action's CustomActionData) and whose Target is the formatted expression.
        var setProp = actions["FfSecret_d"];
        Assert.Equal(SetProperty, setProp.Type);
        Assert.Equal("FfSecret", setProp.Source);
        Assert.Equal("[MY_SECRET]", setProp.Target);

        // The SetProperty must run before the deferred action reads CustomActionData.
        Assert.True(sequence["FfSecret_d"] < sequence["FfSecret"],
            "SetProperty action must be sequenced before the deferred action that reads its value");
    }

    [Fact]
    public void FirewallRule_ProducesLiveCreateRollbackAndRemoveActions()
    {
        using var scratch = new Scratch();

        var firewall = new FirewallExtension();
        firewall.AddRule(r => r
            .Id("Web")
            .Name("Web Server")
            .Port("8080")
            .Direction(FirewallDirection.Inbound)
            .Protocol(FirewallProtocol.Tcp));

        var package = MinimalPackage(scratch, "FirewallLiveApp");
        var result = new MsiCompiler(new WindowsFileSystem()).Use(firewall).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        using var db = Open(result.Value);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        var install = actions["Fw_Web"];
        Assert.Contains("New-NetFirewallRule", install.Target, StringComparison.Ordinal);
        Assert.Contains("-Name 'Fw_Web'", install.Target, StringComparison.Ordinal);
        Assert.Contains("-LocalPort '8080'", install.Target, StringComparison.Ordinal);
        Assert.True((install.Type & InScript) != 0 && (install.Type & NoImpersonate) != 0,
            "firewall install action must be deferred + SYSTEM");

        Assert.Contains("Remove-NetFirewallRule", actions["Fw_Web_rb"].Target, StringComparison.Ordinal);
        Assert.Contains("Remove-NetFirewallRule", actions["Fw_Web_un"].Target, StringComparison.Ordinal);

        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.InRange(sequence["Fw_Web"], init + 1, finalize - 1);
        Assert.InRange(sequence["Fw_Web_rb"], init + 1, finalize - 1);
        Assert.InRange(sequence["Fw_Web_un"], init + 1, finalize - 1);
    }

    [Fact]
    public void LongCommandOver255Chars_RoundTripsIntactThroughCompiledMsi()
    {
        using var scratch = new Scratch();

        // A realistic rule (remote port range + local address + profiles) produces a command line
        // well over the CustomAction.Target CHAR(255) declaration. Windows Installer does not enforce
        // that width on insert, so it must survive intact — if it were ever truncated the deferred
        // action would run a corrupt command, the exact silent-failure class this seam exists to
        // prevent. This test is the guard for that assumption.
        var firewall = new FirewallExtension();
        firewall.AddRule(r => r
            .Id("AllowRange").Name("My App Range")
            .Protocol(FirewallProtocol.Tcp).Port("8080")
            .RemotePort("1024-65535").LocalAddress("192.168.1.10")
            .Direction(FirewallDirection.Inbound).Action(FirewallRuleAction.Allow)
            .Profile(FirewallProfile.All));

        var package = MinimalPackage(scratch, "FirewallLongCmdApp");
        var result = new MsiCompiler(new WindowsFileSystem()).Use(firewall).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        using var db = Open(result.Value);
        var target = QueryCustomActions(db)["Fw_AllowRange"].Target;

        Assert.True(target.Length > 255, $"expected a >255-char command, got {target.Length}");
        Assert.Contains("-RemotePort '1024-65535'", target, StringComparison.Ordinal);
        Assert.Contains("-LocalAddress '192.168.1.10'", target, StringComparison.Ordinal);
        Assert.EndsWith("\"", target, StringComparison.Ordinal); // closing quote intact ⇒ not truncated
    }

    /// <summary>
    /// Proves the firewall rule is genuinely created on install and removed on uninstall — not merely
    /// authored into the MSI. Gated behind <c>FALKFORGE_E2E=1</c> AND administrator elevation because
    /// it runs a real per-machine msiexec install that touches the Windows Firewall. Honestly skips
    /// (never a silent fake pass) when the gate is closed.
    /// </summary>
    [Fact]
    public void FirewallRule_IsCreatedThenRemoved_OnRealInstall()
    {
        if (Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real firewall install e2e is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (!IsElevated())
            Assert.Skip("Real firewall install requires administrator elevation; run the test host elevated.");

        using var scratch = new Scratch();

        const string ruleName = "Fw_E2eWeb";
        var firewall = new FirewallExtension();
        firewall.AddRule(r => r
            .Id("E2eWeb")
            .Name("FalkForge E2E Web")
            .Port("18080")
            .Direction(FirewallDirection.Inbound)
            .Protocol(FirewallProtocol.Tcp));

        var package = MinimalPackage(scratch, "FirewallE2eApp");
        var result = new MsiCompiler(new WindowsFileSystem()).Use(firewall).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        string msi = result.Value;

        // Clean any leftover from a previous aborted run.
        RemoveRuleIfPresent(ruleName);
        try
        {
            int install = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.True(install is 0 or 3010, $"msiexec install exit code {install}");
            Assert.True(FirewallRuleExists(ruleName), "firewall rule was not created by the deferred action");

            int uninstall = RunMsiExec($"/x \"{msi}\" /qn /norestart");
            Assert.True(uninstall is 0 or 3010, $"msiexec uninstall exit code {uninstall}");
            Assert.False(FirewallRuleExists(ruleName), "firewall rule was not removed on uninstall");
        }
        finally
        {
            RemoveRuleIfPresent(ruleName);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Result<string> Compile(Scratch scratch, string name, IFalkForgeExtension extension)
        => new MsiCompiler(new WindowsFileSystem()).Use(extension).Compile(MinimalPackage(scratch, name), scratch.OutputDir);

    private static MsiDatabase Open(string path)
    {
        var dbResult = MsiDatabase.Open(path, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
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

    private static PackageModel MinimalPackage(Scratch scratch, string name)
    {
        var sourceFile = Path.Combine(scratch.SourceDir, "app.exe");
        File.WriteAllText(sourceFile, "payload for execution-step emission test");

        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / name));
        });
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

    private static bool FirewallRuleExists(string ruleName)
        => RunPowerShellSucceeds($"if (Get-NetFirewallRule -Name '{ruleName}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}");

    private static void RemoveRuleIfPresent(string ruleName)
        => RunPowerShellSucceeds($"Remove-NetFirewallRule -Name '{ruleName}' -ErrorAction SilentlyContinue; exit 0");

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

    private sealed class FakeExecutionExtension(string name, params ExecutionStep[] steps) : IFalkForgeExtension
    {
        public string Name { get; } = name;

        public void Register(IExtensionRegistry registry)
            => registry.RegisterExecutionContributor(new FakeExecutionContributor(steps));

        private sealed class FakeExecutionContributor(IReadOnlyList<ExecutionStep> steps) : IExecutionContributor
        {
            public IReadOnlyList<ExecutionStep> GetExecutionSteps(ExtensionContext context) => steps;
        }
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"ExecEmit_{Guid.NewGuid():N}");

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
