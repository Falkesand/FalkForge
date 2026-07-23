using System.Runtime.Versioning;
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
