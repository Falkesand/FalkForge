using System.Diagnostics;
using System.Globalization;
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
/// consumer requiring a provider "within version range [a,b)" was metadata only.
///
/// <para>
/// The check is authored as: RegLocator + AppSearch (read the provider's registered version from
/// HKLM into a property) → an immediate JScript custom action (Type 5, script in the Binary table)
/// that does a REAL component-wise numeric comparison and sets a fail property → a Type 19 custom
/// action that aborts with a message, conditioned on the fail property. A static MSI LaunchCondition
/// cannot be used because MSI condition operators compare lexicographically, not by version.
/// </para>
///
/// These tests open the real compiled MSI (via msi.dll) and assert the rows and their sequencing
/// before <c>InstallInitialize</c> (execute) / <c>ExecuteAction</c> (UI).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DependencyVersionEnforcementTests
{
    [Fact]
    public void VersionRangeConsumer_EmitsBlockingCheck_SequencedBeforeCommit_InBothSequences()
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
        var regRows = db.QueryRows("SELECT `Signature_`, `Root`, `Key`, `Name`, `Type` FROM `RegLocator`", 5);
        Assert.True(regRows.IsSuccess, regRows.IsFailure ? regRows.Error.Message : "");
        var regRow = Assert.Single(regRows.Value);
        string signature = regRow[0]!;
        Assert.Equal("2", regRow[1]); // HKEY_LOCAL_MACHINE
        Assert.Equal(@"SOFTWARE\Classes\Installer\Dependencies\Acme.Foo", regRow[2]);
        Assert.Equal("Version", regRow[3]);
        Assert.Equal("2", regRow[4]); // msidbLocatorTypeRawValue

        // AppSearch: binds a property to that RegLocator signature.
        var appSearchRows = db.QueryRows("SELECT `Property`, `Signature_` FROM `AppSearch`", 2);
        Assert.True(appSearchRows.IsSuccess, appSearchRows.IsFailure ? appSearchRows.Error.Message : "");
        var appSearchRow = Assert.Single(appSearchRows.Value);
        string property = appSearchRow[0]!;
        Assert.StartsWith("FALKDEP", property, StringComparison.Ordinal);
        Assert.Equal(signature, appSearchRow[1]);

        // CustomAction: an immediate JScript evaluator (Type 5, script in Binary) + a Type 19
        // abort-with-message action.
        var caRows = db.QueryRows("SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`", 4);
        Assert.True(caRows.IsSuccess, caRows.IsFailure ? caRows.Error.Message : "");
        var evalRow = Assert.Single(caRows.Value, r => r[1] == "5");
        var abortRow = Assert.Single(caRows.Value, r => r[1] == "19");
        string evalAction = evalRow[0]!;
        string abortAction = abortRow[0]!;
        string binaryName = evalRow[2]!; // Source = Binary key
        Assert.StartsWith("FalkDepChk", evalAction, StringComparison.Ordinal);
        Assert.StartsWith("FalkDepErr", abortAction, StringComparison.Ordinal);

        // The abort message names the provider, the consumer, and shows the detected version token.
        string message = abortRow[3]!;
        Assert.Contains("Acme.Foo", message, StringComparison.Ordinal);
        Assert.Contains("Acme.App", message, StringComparison.Ordinal);
        Assert.Contains($"[{property}]", message, StringComparison.Ordinal);

        // The JScript comparison body is stored in the Binary table and reads the property and
        // compares against the (four-part-normalized) bounds — the real numeric comparison MSI
        // conditions cannot do.
        string script = ReadBinaryScript(db, binaryName);
        Assert.Contains($"Session.Property(\"{property}\")", script, StringComparison.Ordinal);
        Assert.Contains("1.0.0.0", script, StringComparison.Ordinal);
        Assert.Contains("2.0.0.0", script, StringComparison.Ordinal);

        // Sequenced after AppSearch and before commit in BOTH sequences. InstallExecuteSequence's
        // commit point is InstallInitialize; InstallUISequence has no InstallInitialize action —
        // its handoff to the execute sequence is ExecuteAction.
        AssertActionsSequencedBeforeAnchor(db, "InstallExecuteSequence", evalAction, abortAction, "InstallInitialize");
        AssertActionsSequencedBeforeAnchor(db, "InstallUISequence", evalAction, abortAction, "ExecuteAction");

        // The abort action is conditioned on the fail property (only fires when the check failed),
        // and the evaluator is skipped during a full uninstall.
        var abortSeqCondition = QuerySequenceCondition(db, "InstallExecuteSequence", abortAction);
        Assert.StartsWith("FALKDEPF", abortSeqCondition, StringComparison.Ordinal);
        var evalSeqCondition = QuerySequenceCondition(db, "InstallExecuteSequence", evalAction);
        Assert.Equal("REMOVE<>\"ALL\"", evalSeqCondition);
    }

    [Fact]
    public void MultipleVersionRangeConsumers_EmitDistinctNonCollidingChecks()
    {
        using var scratch = new Scratch();

        var dependency = new DependencyExtension();
        dependency.Requires("Acme.Foo", consumer => consumer
            .ConsumerKey("Acme.App").ComponentRef("MainComponent")
            .MinVersion("1.0.0.0").MinInclusive());
        dependency.Requires("Acme.Bar", consumer => consumer
            .ConsumerKey("Acme.App").ComponentRef("MainComponent")
            .MaxVersion("3.0.0.0").MaxExclusive());

        using var db = Compile(scratch, "DepMultiApp", c => c.Use(dependency));

        // Two distinct RegLocator signatures, two evaluators, two abort actions — the content-hash
        // suffix keeps them collision-free.
        var regRows = db.QueryRows("SELECT `Signature_` FROM `RegLocator`", 1);
        Assert.True(regRows.IsSuccess, regRows.IsFailure ? regRows.Error.Message : "");
        Assert.Equal(2, regRows.Value.Count);
        Assert.Equal(2, regRows.Value.Select(r => r[0]).Distinct(StringComparer.Ordinal).Count());

        var caRows = db.QueryRows("SELECT `Action`, `Type` FROM `CustomAction`", 2);
        Assert.True(caRows.IsSuccess, caRows.IsFailure ? caRows.Error.Message : "");
        Assert.Equal(2, caRows.Value.Count(r => r[1] == "5"));
        Assert.Equal(2, caRows.Value.Count(r => r[1] == "19"));

        // Distinct sequence numbers per authored custom action (no two share a number).
        var seqRows = db.QueryRows("SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`", 2);
        Assert.True(seqRows.IsSuccess, seqRows.IsFailure ? seqRows.Error.Message : "");
        var falkRows = seqRows.Value
            .Where(r => (r[0] ?? "").StartsWith("FalkDep", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(4, falkRows.Count); // 2 evaluators + 2 aborts
        Assert.Equal(4, falkRows.Select(r => r[1]).Distinct(StringComparer.Ordinal).Count());
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
        // MSI-time check — no RegLocator rows and no version-check custom actions.
        var regLocatorTable = db.QueryRows("SELECT `Signature_` FROM `RegLocator`", 1);
        Assert.True(regLocatorTable.IsFailure || regLocatorTable.Value.Count == 0);

        var caRows = db.QueryRows("SELECT `Action` FROM `CustomAction`", 1);
        // CustomAction table may be absent entirely when nothing authored a custom action.
        if (caRows.IsSuccess)
            Assert.DoesNotContain(caRows.Value, r => (r[0] ?? "").StartsWith("FalkDep", StringComparison.Ordinal));
    }

    /// <summary>
    /// Proves the check genuinely blocks/allows a REAL msiexec install (including multi-digit
    /// version components, which a lexicographic MSI condition would mis-compare), not just that
    /// the right table rows exist. Gated behind <c>FALKFORGE_E2E=1</c> AND administrator elevation,
    /// matching <see cref="UtilExecutionEmissionTests.FileShare_IsCreatedThenRemoved_OnRealInstall"/>
    /// — a real per-machine msiexec install needs HKLM write access. Honestly skips (never a silent
    /// fake pass) when the gate is closed. Seeds the provider side directly via the registry (the
    /// exact HKLM path/value DependencyTableContributor itself writes) so the real msiexec engine
    /// evaluates AppSearch + the JScript comparison against real machine state.
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
            .MinVersion("9.0.0.0").MinInclusive()
            .MaxVersion("11.0.0.0").MaxExclusive());

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
            // Provider absent entirely — the check must abort before anything commits.
            int blockedMissing = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.False(blockedMissing is 0 or 3010,
                $"install should have been blocked (provider missing) but exit code was {blockedMissing}");

            // Provider present but below the required [9.0.0.0, 11.0.0.0) range.
            WriteProviderVersion(providerKey, "8.5.0.0");
            int blockedLow = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.False(blockedLow is 0 or 3010,
                $"install should have been blocked (version below range) but exit code was {blockedLow}");

            // Multi-digit in-range value (10.x): a lexicographic comparison would wrongly reject
            // 10.0.0.0 against min 9.0.0.0 ('1' < '9') — the JScript numeric comparison must ALLOW.
            WriteProviderVersion(providerKey, "10.0.0.0");
            int allowed = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.True(allowed is 0 or 3010, $"install should have proceeded (10.0.0.0 in range) but exit code was {allowed}");

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

    private static string ReadBinaryScript(MsiDatabase db, string binaryName)
    {
        var bytes = db.ReadStream(
            $"SELECT `Data` FROM `Binary` WHERE `Name`='{binaryName}'", fieldCount: 1, streamField: 1);
        Assert.True(bytes.IsSuccess, bytes.IsFailure ? bytes.Error.Message : "");
        return System.Text.Encoding.UTF8.GetString(bytes.Value);
    }

    private static string QuerySequenceCondition(MsiDatabase db, string sequenceTable, string action)
    {
        var rows = db.QueryRows(
            $"SELECT `Action`, `Condition` FROM `{sequenceTable}` WHERE `Action`='{action}'", 2);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");
        var row = Assert.Single(rows.Value);
        return row[1] ?? string.Empty;
    }

    private static void AssertActionsSequencedBeforeAnchor(
        MsiDatabase db, string sequenceTable, string evalAction, string abortAction, string anchorAction)
    {
        var rows = db.QueryRows($"SELECT `Action`, `Sequence` FROM `{sequenceTable}`", 2);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");

        int? evalSeq = null;
        int? abortSeq = null;
        int? anchorSeq = null;
        int? appSearchSeq = null;
        foreach (var row in rows.Value)
        {
            if (row[1] is null || !int.TryParse(row[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seq))
                continue;
            if (row[0] == evalAction) evalSeq = seq;
            if (row[0] == abortAction) abortSeq = seq;
            if (row[0] == anchorAction) anchorSeq = seq;
            if (row[0] == "AppSearch") appSearchSeq = seq;
        }

        Assert.NotNull(evalSeq);
        Assert.NotNull(abortSeq);
        Assert.NotNull(anchorSeq);
        Assert.NotNull(appSearchSeq);
        Assert.True(appSearchSeq < evalSeq, $"{sequenceTable}: AppSearch must run before the evaluator.");
        Assert.True(evalSeq < abortSeq, $"{sequenceTable}: evaluator must run before the abort action.");
        Assert.True(abortSeq < anchorSeq,
            $"{sequenceTable}: the check ({abortSeq}) must run before {anchorAction} ({anchorSeq}).");
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
