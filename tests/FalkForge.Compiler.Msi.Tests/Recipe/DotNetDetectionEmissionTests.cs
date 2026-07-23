using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using FalkForge.Builders;
using FalkForge.Extensions.DotNet;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Proves the DotNet extension's runtime search reaches the compiled MSI as REAL MSI-native
/// detection — <c>Signature</c> + <c>DrLocator</c> + <c>AppSearch</c>, all of which the built-in
/// <c>AppSearch</c> standard action evaluates natively at install time, no custom action required
/// (unlike the Dependency extension's version-RANGE check, which needs JScript because a min-version
/// file search is something MSI's own machinery already does). These tests open the real compiled
/// MSI (via msi.dll) and assert the rows read back exactly as planned.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DotNetDetectionEmissionTests
{
    [Fact]
    public void EmitsSignatureDrLocatorAppSearch_ReadBackFromCompiledMsi()
    {
        using var scratch = new Scratch();

        var dotnet = new DotNetExtension();
        var addResult = dotnet.AddSearch(new DotNetCoreSearchModel
        {
            RuntimeType = DotNetRuntimeType.Runtime,
            Platform = DotNetPlatform.X64,
            MinimumVersion = new Version(8, 0, 0),
            VariableName = "DOTNET8_FOUND",
            Message = ".NET 8.0 Runtime (x64) or later is required.",
        });
        Assert.True(addResult.IsSuccess, addResult.IsFailure ? addResult.Error.Message : "");

        using var db = Compile(scratch, "DotNetDetectApp", c => c.Use(dotnet));

        // Signature: describes the sentinel file whose on-disk version proves the shared framework
        // is present and new enough.
        var sigRows = db.QueryRows("SELECT `Signature`, `FileName`, `MinVersion`, `MaxVersion` FROM `Signature`", 4);
        Assert.True(sigRows.IsSuccess, sigRows.IsFailure ? sigRows.Error.Message : "");
        var sigRow = Assert.Single(sigRows.Value);
        string signature = sigRow[0]!;
        Assert.Equal("coreclr.dll", sigRow[1]);
        Assert.Equal("8.0.0.0", sigRow[2]);
        Assert.Null(sigRow[3]);

        // DrLocator: searches the shared-framework directory one level deep (version subdirectories)
        // for the Signature above.
        var drRows = db.QueryRows("SELECT `Signature_`, `Path`, `Depth` FROM `DrLocator`", 3);
        Assert.True(drRows.IsSuccess, drRows.IsFailure ? drRows.Error.Message : "");
        var drRow = Assert.Single(drRows.Value);
        Assert.Equal(signature, drRow[0]);
        Assert.Equal(@"[ProgramFiles64Folder]dotnet\shared\Microsoft.NETCore.App", drRow[1]);
        Assert.Equal("1", drRow[2]);

        // AppSearch: binds the author's own property name to the DrLocator signature — the built-in
        // AppSearch standard action populates DOTNET8_FOUND from this at install time.
        var appSearchRows = db.QueryRows("SELECT `Property`, `Signature_` FROM `AppSearch`", 2);
        Assert.True(appSearchRows.IsSuccess, appSearchRows.IsFailure ? appSearchRows.Error.Message : "");
        var appSearchRow = Assert.Single(appSearchRows.Value);
        Assert.Equal("DOTNET8_FOUND", appSearchRow[0]);
        Assert.Equal(signature, appSearchRow[1]);

        // LaunchCondition: since the model carries a Message, the extension emits its own blocking
        // condition (the JSON authoring path's shape) — merged into the built-in table alongside
        // InstallerTestHost's default NOT NEWERVERSIONFOUND downgrade guard, so filter to our row.
        var lcRows = db.QueryRows(
            "SELECT `Condition`, `Description` FROM `LaunchCondition` WHERE `Condition`='DOTNET8_FOUND'", 2);
        Assert.True(lcRows.IsSuccess, lcRows.IsFailure ? lcRows.Error.Message : "");
        var lcRow = Assert.Single(lcRows.Value);
        Assert.Equal("DOTNET8_FOUND", lcRow[0]);
        Assert.Equal(".NET 8.0 Runtime (x64) or later is required.", lcRow[1]);
    }

    [Fact]
    public void CSharpPath_NoModelMessage_EmitsNoLaunchCondition_ReliesOnPackageRequire()
    {
        using var scratch = new Scratch();

        // C# fluent authoring path: the model carries NO Message (the demo's shape) — the author
        // gates via package.Require(...) themselves.
        var dotnet = new DotNetExtension();
        var addResult = dotnet.AddSearch(new DotNetCoreSearchModel
        {
            RuntimeType = DotNetRuntimeType.Runtime,
            Platform = DotNetPlatform.X64,
            MinimumVersion = new Version(8, 0, 0),
            VariableName = "DOTNET8_FOUND",
        });
        Assert.True(addResult.IsSuccess, addResult.IsFailure ? addResult.Error.Message : "");

        using var db = Compile(
            scratch,
            "DotNetCSharpGateApp",
            c => c.Use(dotnet),
            p => p.Require("DOTNET8_FOUND", ".NET 8.0 Runtime (x64) or later is required."));

        // Exactly ONE LaunchCondition row for DOTNET8_FOUND (from package.Require) — proves the
        // extension's own contributor did NOT also emit one, which would collide on the Condition
        // primary key.
        var rows = db.QueryRows(
            "SELECT `Condition`, `Description` FROM `LaunchCondition` WHERE `Condition`='DOTNET8_FOUND'", 2);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");
        var row = Assert.Single(rows.Value);
        Assert.Equal(".NET 8.0 Runtime (x64) or later is required.", row[1]);
    }

    [Fact]
    public void JsonStylePath_ModelMessage_EmitsLaunchCondition()
    {
        using var scratch = new Scratch();

        // JSON authoring path shape: the model carries a Message and the package has NO Require —
        // the extension's own contributor must be the sole source of the gate.
        var dotnet = new DotNetExtension();
        var addResult = dotnet.AddSearch(new DotNetCoreSearchModel
        {
            RuntimeType = DotNetRuntimeType.Runtime,
            Platform = DotNetPlatform.X64,
            MinimumVersion = new Version(8, 0, 0),
            VariableName = "DOTNET8_FOUND",
            Message = ".NET 8.0 Runtime (x64) or later is required.",
        });
        Assert.True(addResult.IsSuccess, addResult.IsFailure ? addResult.Error.Message : "");

        using var db = Compile(scratch, "DotNetJsonGateApp", c => c.Use(dotnet));

        var rows = db.QueryRows(
            "SELECT `Condition`, `Description` FROM `LaunchCondition` WHERE `Condition`='DOTNET8_FOUND'", 2);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");
        var row = Assert.Single(rows.Value);
        Assert.Equal(".NET 8.0 Runtime (x64) or later is required.", row[1]);
    }

    /// <summary>
    /// Proves the MSI-native search genuinely blocks/allows a REAL msiexec install against REAL
    /// machine state, not just that the right table rows exist (that is
    /// <see cref="EmitsSignatureDrLocatorAppSearch_ReadBackFromCompiledMsi"/>'s job). Gated behind
    /// <c>FALKFORGE_E2E=1</c> AND <c>FALKFORGE_REAL_SYSTEM_E2E=1</c> AND administrator elevation,
    /// matching <see cref="DependencyVersionEnforcementTests.VersionRangeCheck_BlocksRealInstall_WhenProviderMissingOrOutOfRange_AllowsWhenInRange"/>
    /// — a real per-machine msiexec install needs HKLM/Program-Files write access.
    /// <para>
    /// Unlike the Dependency e2e (which seeds a synthetic registry value), there is no safe way to
    /// fabricate a real <c>coreclr.dll</c> with an arbitrary file-version resource from a unit test —
    /// so this test relies on the REAL, already-installed .NET runtime on the host running it (the
    /// test host itself is a .NET 10 process, so a real <c>coreclr.dll</c> genuinely exists under
    /// <c>[ProgramFiles64Folder]dotnet\shared\Microsoft.NETCore.App\</c>). A search for
    /// <c>Runtime</c>/<c>X64</c> with an absurdly high minimum version (never satisfiable by any real
    /// release) proves the block path; the same search with a trivially low minimum version proves the
    /// allow path against that same real installation. This is honestly narrower than the Dependency
    /// e2e (it cannot prove an ABSENT runtime blocks install, only that a too-new requirement does) —
    /// documented here rather than silently assumed.
    /// </para>
    /// </summary>
    [Fact]
    public void DotNetDetection_BlocksInstall_WhenVersionRequirementUnmet_AllowsWhenSatisfied()
    {
        if (Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real .NET detection install e2e is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (Environment.GetEnvironmentVariable("FALKFORGE_REAL_SYSTEM_E2E") != "1")
            Assert.Skip("Real .NET detection install mutates machine-wide state: set " +
                        "FALKFORGE_REAL_SYSTEM_E2E=1 on a machine you own to run it.");
        if (!IsElevated())
            Assert.Skip("Real .NET detection install requires administrator elevation; run the test host elevated.");

        using var scratchBlocked = new Scratch();
        var blockedDotNet = new DotNetExtension();
        var blockedAdd = blockedDotNet.AddSearch(new DotNetCoreSearchModel
        {
            RuntimeType = DotNetRuntimeType.Runtime,
            Platform = DotNetPlatform.X64,
            MinimumVersion = new Version(999, 0, 0),
            VariableName = "DOTNET_E2E_FOUND",
            Message = "An impossibly high .NET version is required (deliberately unsatisfiable).",
        });
        Assert.True(blockedAdd.IsSuccess, blockedAdd.IsFailure ? blockedAdd.Error.Message : "");
        string blockedMsi = CompileToMsi(scratchBlocked, "DotNetE2eBlockedApp", blockedDotNet);
        int blocked = RunMsiExec($"/i \"{blockedMsi}\" /qn /norestart");
        Assert.False(blocked is 0 or 3010,
            $"install should have been blocked (version requirement unsatisfiable) but exit code was {blocked}");

        using var scratchAllowed = new Scratch();
        var allowedDotNet = new DotNetExtension();
        var allowedAdd = allowedDotNet.AddSearch(new DotNetCoreSearchModel
        {
            RuntimeType = DotNetRuntimeType.Runtime,
            Platform = DotNetPlatform.X64,
            MinimumVersion = new Version(1, 0, 0),
            VariableName = "DOTNET_E2E_FOUND",
            Message = "A trivially low .NET version is required (deliberately always satisfied).",
        });
        Assert.True(allowedAdd.IsSuccess, allowedAdd.IsFailure ? allowedAdd.Error.Message : "");
        string allowedMsi = CompileToMsi(scratchAllowed, "DotNetE2eAllowedApp", allowedDotNet);
        int allowed = RunMsiExec($"/i \"{allowedMsi}\" /qn /norestart");
        Assert.True(allowed is 0 or 3010, $"install should have proceeded (trivial version requirement) but exit code was {allowed}");

        int uninstall = RunMsiExec($"/x \"{allowedMsi}\" /qn /norestart");
        Assert.True(uninstall is 0 or 3010, $"uninstall exit code {uninstall}");
    }

    private static string CompileToMsi(Scratch scratch, string name, DotNetExtension dotnet)
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
        });
        var compiler = new MsiCompiler(new WindowsFileSystem());
        compiler.Use(dotnet);
        var compileResult = compiler.Compile(package, scratch.OutputDir);
        Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");
        return compileResult.Value;
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

    private static MsiDatabase Compile(
        Scratch scratch, string name, Action<MsiCompiler> attach, Action<PackageBuilder>? configurePackage = null)
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.UseDialogSet(MsiDialogSet.Minimal);
            configurePackage?.Invoke(p);
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
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"DotNetDetectEmit_{Guid.NewGuid():N}");

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
