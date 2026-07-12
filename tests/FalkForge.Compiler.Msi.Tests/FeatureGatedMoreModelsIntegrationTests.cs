using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Proves that gating a shortcut, environment variable, INI file entry, or file association to a
/// feature via FeatureBuilder actually drives the compiled MSI: the entry's synthesized component
/// must appear in the FeatureComponents table under the declaring feature, and NOT under an
/// unrelated sibling feature. FeatureRef alone (an in-memory model field) proves nothing — only
/// the compiled table content does, which is what this test asserts. Mirrors
/// FeatureGatedServiceRegistryIntegrationTests, which proves the same contract for services and
/// registry entries.
///
/// Permission and Font are covered separately (<see cref="FeatureGatedPermissionConditionTests"/>,
/// <see cref="FeatureGatedFontViaFileTests"/>) because neither the real MSI LockPermissions/
/// MsiLockPermissionsEx tables nor the Font table carry a Component_/Feature_ column of their own
/// — see those files for the honest, structurally-correct mechanism used for each.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FeatureGatedMoreModelsIntegrationTests
{
    [Fact]
    public void Compile_ShortcutGatedToFeature_FeatureComponentsMapsShortcutUnderThatFeatureOnly()
    {
        RunGatedCompile("FeatSc", (package, sourceFile) =>
        {
            package.Feature("FeatureA", f =>
                f.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatSc")));
            package.Feature("FeatureB", f =>
                f.Shortcut("GatedShortcut", "completely_unrelated_target.exe").OnDesktop());
        },
        db =>
        {
            var rows = db.QueryRows(
                "SELECT `Shortcut`, `Name`, `Component_` FROM `Shortcut` WHERE `Name` = 'GatedShortcut'", 3).Value;
            return Assert.Single(rows)[2]!;
        });
    }

    [Fact]
    public void Compile_EnvironmentVariableGatedToFeature_FeatureComponentsMapsEntryUnderThatFeatureOnly()
    {
        RunGatedCompile("FeatEnv", (package, sourceFile) =>
        {
            package.Feature("FeatureA", f =>
                f.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatEnv")));
            package.Feature("FeatureB", f =>
                f.EnvironmentVariable("GATED_VAR_XYZ", @"C:\somewhere"));
        },
        db =>
        {
            var rows = db.QueryRows(
                "SELECT `Environment`, `Name`, `Component_` FROM `Environment`", 3).Value;
            return Assert.Single(rows, r => r[1]!.Contains("GATED_VAR_XYZ"))[2]!;
        });
    }

    [Fact]
    public void Compile_IniFileGatedToFeature_FeatureComponentsMapsEntryUnderThatFeatureOnly()
    {
        RunGatedCompile("FeatIni", (package, sourceFile) =>
        {
            package.Feature("FeatureA", f =>
                f.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatIni")));
            package.Feature("FeatureB", f =>
                f.IniFile("gated_unrelated.ini", ini => ini.Section("S").Key("K").Value("v")));
        },
        db =>
        {
            var rows = db.QueryRows(
                "SELECT `IniFile`, `FileName`, `Component_` FROM `IniFile` WHERE `FileName` = 'gated_unrelated.ini'", 3).Value;
            return Assert.Single(rows)[2]!;
        });
    }

    [Fact]
    public void Compile_FileAssociationGatedToFeature_FeatureComponentsMapsEntryUnderThatFeatureOnly()
    {
        var featureComponentRows = RunGatedCompile("FeatFas", (package, sourceFile) =>
        {
            package.Feature("FeatureA", f =>
                f.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatFas")));
            package.Feature("FeatureB", f =>
                f.FileAssociation(".gatedext", a => a.ProgId("App.GatedExt")));
        },
        db =>
        {
            var rows = db.QueryRows(
                "SELECT `Extension`, `Component_`, `Feature_` FROM `Extension` WHERE `Extension` = 'gatedext'", 3).Value;
            var row = Assert.Single(rows);
            // Extension carries its own Feature_ column directly — assert it too.
            Assert.Equal("FeatureB", row[2]);
            return row[1]!;
        });
    }

    /// <summary>
    /// Shared compile-and-assert harness: builds a two-feature package (FeatureA gets a plain
    /// file so it owns the first resolved component — the wrong fallback target if gating were
    /// broken — FeatureB gets the entry under test), compiles it, and asserts the component
    /// returned by <paramref name="extractComponentId"/> appears in FeatureComponents under
    /// FeatureB and not FeatureA.
    /// </summary>
    private static string RunGatedCompile(
        string name,
        Action<Builders.PackageBuilder, string> configure,
        Func<MsiDatabase, string> extractComponentId)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiFeat_{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake exe for feature-gating test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = name;
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                configure(p, sourceFile);
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var componentId = extractComponentId(db);
            Assert.NotNull(componentId);

            var featureComponentRows = db.QueryRows(
                "SELECT `Feature_`, `Component_` FROM `FeatureComponents`", 2).Value;

            Assert.Contains(featureComponentRows, r => r[1] == componentId && r[0] == "FeatureB");
            Assert.DoesNotContain(featureComponentRows, r => r[1] == componentId && r[0] == "FeatureA");

            return componentId;
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
