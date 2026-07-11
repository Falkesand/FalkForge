using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using FalkForge.Extensions.Dependency;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Microsoft.Win32;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Proves the Dependency extension's version-range consumer check reaches the compiled MSI and
/// actually blocks install before anything commits. Before this feature, <c>DependencyChecker</c>
/// contained the exact comparison logic but nothing wired it into the compile pipeline — a
/// consumer requiring a provider "within version range [a,b)" was metadata only. This test
/// compiles a package with such a consumer and inspects the real MSI (via msi.dll, not just the
/// in-memory recipe) for the RegLocator/AppSearch/LaunchCondition rows that enforce it, and for
/// their sequencing relative to <c>InstallInitialize</c> — the point installation commits — in
/// both <c>InstallExecuteSequence</c> and <c>InstallUISequence</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DependencyVersionEnforcementTests
{
    [Fact]
    public void VersionRangeConsumer_EmitsBlockingCheck_SequencedBeforeInstallInitialize_InBothSequences()
    {
        using var scratch = new Scratch();

        var dependency = new DependencyExtension();
        dependency.Requires("Acme.Foo", consumer => consumer
            .ConsumerKey("Acme.App")
            .ComponentRef("MainComponent")
            .MinVersion("1.0.0.0").MinInclusive()
            .MaxVersion("2.0.0.0").MaxExclusive());

        using var db = Compile(scratch, "DepEnforceApp", c => c.Use(dependency));

        // RegLocator: reads the provider's registered Version value from the same HKLM path
        // DependencyTableContributor writes to.
        var regRows = db.QueryRows("SELECT `Root`, `Key`, `Name`, `Type` FROM `RegLocator`", 4);
        Assert.True(regRows.IsSuccess, regRows.IsFailure ? regRows.Error.Message : "");
        var regRow = Assert.Single(regRows.Value);
        Assert.Equal("2", regRow[0]); // HKEY_LOCAL_MACHINE
        Assert.Equal(@"SOFTWARE\Classes\Installer\Dependencies\Acme.Foo", regRow[1]);
        Assert.Equal("Version", regRow[2]);
        Assert.Equal("2", regRow[3]); // msidbLocatorTypeRawValue

        // AppSearch: binds a property to that RegLocator signature so LaunchConditions can read it.
        var appSearchRows = db.QueryRows("SELECT `Property`, `Signature_` FROM `AppSearch`", 2);
        Assert.True(appSearchRows.IsSuccess, appSearchRows.IsFailure ? appSearchRows.Error.Message : "");
        var appSearchRow = Assert.Single(appSearchRows.Value);
        string property = appSearchRow[0]!;
        Assert.False(string.IsNullOrEmpty(property));

        // LaunchCondition: aborts unless the property is present and within [1.0.0.0, 2.0.0.0).
        // The table may also carry an unrelated default major-upgrade downgrade guard row
        // ("NOT NEWERVERSIONFOUND"), so select ours by its synthetic property marker rather than
        // assuming it is the only row.
        var launchRows = db.QueryRows("SELECT `Condition`, `Description` FROM `LaunchCondition`", 2);
        Assert.True(launchRows.IsSuccess, launchRows.IsFailure ? launchRows.Error.Message : "");
        var launchRow = Assert.Single(launchRows.Value, row => (row[0] ?? string.Empty).Contains(property, StringComparison.Ordinal));
        string condition = launchRow[0]!;
        string message = launchRow[1]!;
        Assert.Contains(property + "<>\"\"", condition, StringComparison.Ordinal);
        Assert.Contains(property + ">=\"1.0.0.0\"", condition, StringComparison.Ordinal);
        Assert.Contains(property + "<\"2.0.0.0\"", condition, StringComparison.Ordinal);
        Assert.Contains("Acme.Foo", message, StringComparison.Ordinal);
        Assert.Contains("Acme.App", message, StringComparison.Ordinal);
        Assert.Contains($"[{property}]", message, StringComparison.Ordinal);

        // Sequenced before commit in BOTH sequences — this package has UseDialogSet so
        // InstallUISequence carries its own baseline too. InstallExecuteSequence's commit point is
        // InstallInitialize; InstallUISequence has no InstallInitialize action at all (that only
        // exists in the execute sequence) — its handoff point to the execute sequence is
        // ExecuteAction, so that is the anchor there instead.
        AssertLaunchConditionsBeforeAnchor(db, "InstallExecuteSequence", "InstallInitialize");
        AssertLaunchConditionsBeforeAnchor(db, "InstallUISequence", "ExecuteAction");
    }

    [Fact]
    public void PresenceOnlyConsumer_EmitsNoBlockingCheck()
    {
        using var scratch = new Scratch();

        var dependency = new DependencyExtension();
        dependency.Requires("Acme.Bar", consumer => consumer
            .ConsumerKey("Acme.App")
            .ComponentRef("MainComponent"));

        using var db = Compile(scratch, "DepPresenceOnlyApp", c => c.Use(dependency));

        // A presence-only requirement (no MinVersion/MaxVersion) is out of scope for this
        // MSI-time check — no RegLocator/AppSearch/LaunchCondition rows should be authored.
        var regLocatorTable = db.QueryRows("SELECT `Signature_` FROM `RegLocator`", 1);
        Assert.True(regLocatorTable.IsFailure || regLocatorTable.Value.Count == 0);

        // LaunchCondition may still carry an unrelated baseline row (e.g. a default major-upgrade
        // downgrade guard), so assert on the absence of our synthetic FALKDEP marker specifically
        // rather than requiring the whole table to be empty.
        var launchRows = db.QueryRows("SELECT `Condition` FROM `LaunchCondition`", 1);
        Assert.True(launchRows.IsSuccess, launchRows.IsFailure ? launchRows.Error.Message : "");
        Assert.DoesNotContain(launchRows.Value, row => (row[0] ?? string.Empty).Contains("FALKDEP", StringComparison.Ordinal));
    }

    /// <summary>
    /// Proves the check genuinely blocks/allows a REAL msiexec install, not just that the right
    /// table rows exist. Gated behind <c>FALKFORGE_E2E=1</c> AND administrator elevation, matching
    /// <see cref="UtilExecutionEmissionTests.FileShare_IsCreatedThenRemoved_OnRealInstall"/> —
    /// a real per-machine msiexec install needs HKLM write access. Honestly skips (never a
    /// silent fake pass) when the gate is closed. Seeds the "provider" side directly via the
    /// registry (the exact HKLM path/value <see cref="DependencyTableContributor"/> itself writes)
    /// rather than compiling and installing a second MSI — this still exercises the real msiexec
    /// engine's AppSearch + RegLocator + LaunchConditions evaluation against real machine state.
    /// </summary>
    [Fact]
    public void VersionRangeCheck_BlocksRealInstall_WhenProviderMissingOrOutOfRange_AllowsWhenInRange()
    {
        if (Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real dependency version-check install e2e is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (!IsElevated())
            Assert.Skip("Real dependency version-check install requires administrator elevation; run the test host elevated.");

        using var scratch = new Scratch();
        const string providerKey = "FalkForge.E2eTests.Acme.Provider";

        var dependency = new DependencyExtension();
        dependency.Requires(providerKey, consumer => consumer
            .ConsumerKey("Acme.E2eApp")
            .ComponentRef("MainComponent")
            .MinVersion("1.0.0.0").MinInclusive()
            .MaxVersion("2.0.0.0").MaxExclusive());

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "DepVersionCheckE2eApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
        });
        var compiler = new MsiCompiler(new WindowsFileSystem());
        compiler.Use(dependency);
        var compileResult = compiler.Compile(package, scratch.OutputDir);
        Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");
        string msi = compileResult.Value;

        RemoveProviderKeyIfPresent(providerKey);
        try
        {
            // Provider absent entirely — the LaunchCondition must abort before anything commits.
            int blockedMissing = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.False(blockedMissing is 0 or 3010,
                $"install should have been blocked (provider missing) but exit code was {blockedMissing}");

            // Provider present but below the required [1.0.0.0, 2.0.0.0) range.
            WriteProviderVersion(providerKey, "0.9.0.0");
            int blockedOutOfRange = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.False(blockedOutOfRange is 0 or 3010,
                $"install should have been blocked (version out of range) but exit code was {blockedOutOfRange}");

            // Provider present and within range — install must proceed.
            WriteProviderVersion(providerKey, "1.5.0.0");
            int allowed = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.True(allowed is 0 or 3010, $"install should have proceeded (version in range) but exit code was {allowed}");

            int uninstall = RunMsiExec($"/x \"{msi}\" /qn /norestart");
            Assert.True(uninstall is 0 or 3010, $"uninstall exit code {uninstall}");
        }
        finally
        {
            RemoveProviderKeyIfPresent(providerKey);
        }
    }

    private static void WriteProviderVersion(string providerKey, string version)
    {
        using var key = Registry.LocalMachine.CreateSubKey(
            @$"SOFTWARE\Classes\Installer\Dependencies\{providerKey}");
        key.SetValue(null, providerKey);
        key.SetValue("Version", version);
    }

    private static void RemoveProviderKeyIfPresent(string providerKey)
    {
        Registry.LocalMachine.DeleteSubKeyTree(
            @$"SOFTWARE\Classes\Installer\Dependencies\{providerKey}", throwOnMissingSubKey: false);
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

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void AssertLaunchConditionsBeforeAnchor(MsiDatabase db, string sequenceTable, string anchorAction)
    {
        var rows = db.QueryRows($"SELECT `Action`, `Sequence` FROM `{sequenceTable}`", 2);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");

        int? launchConditionsSeq = null;
        int? anchorSeq = null;
        foreach (var row in rows.Value)
        {
            if (row[1] is null || !int.TryParse(row[1], out int seq)) continue;
            if (row[0] == "LaunchConditions") launchConditionsSeq = seq;
            if (row[0] == anchorAction) anchorSeq = seq;
        }

        Assert.NotNull(launchConditionsSeq);
        Assert.NotNull(anchorSeq);
        Assert.True(
            launchConditionsSeq < anchorSeq,
            $"{sequenceTable}: LaunchConditions ({launchConditionsSeq}) must run before {anchorAction} ({anchorSeq}).");
    }

    private static MsiDatabase Compile(Scratch scratch, string name, Action<MsiCompiler> attach)
    {
        // No files are authored on purpose: ComponentTableProducer synthesizes a "MainComponent"
        // placeholder row precisely when the resolved package has no files (see
        // MsiRecipeBuilderTests.Build_empty_pipeline_emits_built_in_tables_with_no_rows), which
        // gives this test a stable, real Component_ target for ComponentRef("MainComponent")
        // without depending on the file/component-id hashing scheme.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.UseDialogSet(MsiDialogSet.Minimal);
        });

        var compiler = new MsiCompiler(new WindowsFileSystem());
        attach(compiler);
        var result = compiler.Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"DepVerEnforce_{Guid.NewGuid():N}");

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
