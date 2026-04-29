using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI;

[SupportedOSPlatform("windows")]
public sealed class DialogSetTests
{
    [Fact]
    public void MinimalDialogSet_EmitsWithoutErrors()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "MinimalApp");
        try
        {
            AssertMsiHasDialogTables(msiPath);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void InstallDirDialogSet_EmitsWithoutErrors()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.InstallDir, "InstallDirApp");
        try
        {
            AssertMsiHasDialogTables(msiPath);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void FeatureTreeDialogSet_EmitsWithoutErrors()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.FeatureTree, "FeatureTreeApp");
        try
        {
            AssertMsiHasDialogTables(msiPath);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void MondoDialogSet_EmitsWithoutErrors()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Mondo, "MondoApp");
        try
        {
            AssertMsiHasDialogTables(msiPath);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void AdvancedDialogSet_EmitsWithoutErrors()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Advanced, "AdvancedApp");
        try
        {
            AssertMsiHasDialogTables(msiPath);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void MinimalDialogSet_HasExpectedDialogCount()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "MinCntApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var rows = db.QueryRows("SELECT `Dialog` FROM `Dialog`", 1);
            Assert.True(rows.IsSuccess, FailMsg(rows));
            Assert.Equal(4, rows.Value.Count); // WelcomeDlg, ProgressDlg, ExitDlg, CancelDlg
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void InstallDirDialogSet_HasExpectedDialogCount()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.InstallDir, "IDCntApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var rows = db.QueryRows("SELECT `Dialog` FROM `Dialog`", 1);
            Assert.True(rows.IsSuccess, FailMsg(rows));
            Assert.Equal(7, rows.Value.Count); // Welcome, License, InstallDir, Progress, Exit, CancelDlg, BrowseDlg
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void MondoDialogSet_HasSetupTypeDialog()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Mondo, "MondoSTApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var dialogs = QueryDialogNames(db);
            Assert.Contains("SetupTypeDlg", dialogs);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void AdvancedDialogSet_HasInstallScopeDialog()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Advanced, "AdvScopeApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var dialogs = QueryDialogNames(db);
            Assert.Contains("InstallScopeDlg", dialogs);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void ControlTable_HasExpectedControlsForMinimal()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "MinCtrlApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var controls = QueryControlTypes(db);
            Assert.Contains("PushButton", controls);
            Assert.Contains("Text", controls);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void ControlEventTable_HasNavigationEventsForInstallDir()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.InstallDir, "IDNavApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var events = QueryControlEvents(db);
            Assert.Contains("NewDialog", events);
            Assert.Contains("EndDialog", events);
            Assert.Contains("SpawnDialog", events);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void TextStyleTable_HasStandardFontEntries()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "TxtApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var styles = QueryTextStyles(db);
            Assert.Contains("DlgFont8", styles);
            Assert.Contains("DlgFontBold8", styles);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void InstallDirTemplate_IncludesPathEditControl()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.InstallDir, "IDPathApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var controls = QueryControlTypes(db);
            Assert.Contains("PathEdit", controls);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void FeatureTreeTemplate_IncludesSelectionTreeControl()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.FeatureTree, "FTTreeApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var controls = QueryControlTypes(db);
            Assert.Contains("SelectionTree", controls);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void MondoTemplate_IncludesSelectionTreeControl()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Mondo, "MondoTreeApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var controls = QueryControlTypes(db);
            Assert.Contains("SelectionTree", controls);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    // --- Helper methods ---

    private static void AssertMsiHasDialogTables(string msiPath)
    {
        using var db = OpenMsi(msiPath);
        AssertTableExists(db, "Dialog");
        AssertTableExists(db, "Control");
        AssertTableExists(db, "ControlEvent");
        AssertTableExists(db, "TextStyle");
        AssertTableExists(db, "UIText");
    }

    private static void AssertTableExists(MsiDatabase db, string tableName)
    {
        var result = db.Execute($"SELECT * FROM `{tableName}`");
        Assert.True(result.IsSuccess, $"Table '{tableName}' not found: {(result.IsFailure ? result.Error.Message : "")}");
    }

    private static string CompileWithDialogSet(MsiDialogSet dialogSet, string appName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"DlgTest_{appName}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var sourceDir = Path.Combine(tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, $"fake content for {appName}");

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = appName;
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / appName));
            p.UseDialogSet(dialogSet);
        });

        var fileSystem = new WindowsFileSystem();
        var compiler = new MsiCompiler(fileSystem);
        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        return result.Value;
    }

    private static MsiDatabase OpenMsi(string msiPath)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
    }

    private static HashSet<string> QueryDialogNames(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Dialog` FROM `Dialog`", 1);
        Assert.True(rows.IsSuccess, FailMsg(rows));
        return new HashSet<string>(rows.Value.Select(r => r[0]!), StringComparer.Ordinal);
    }

    private static HashSet<string> QueryControlTypes(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Type` FROM `Control`", 1);
        Assert.True(rows.IsSuccess, FailMsg(rows));
        return new HashSet<string>(rows.Value.Select(r => r[0]!), StringComparer.Ordinal);
    }

    private static HashSet<string> QueryControlEvents(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Event` FROM `ControlEvent`", 1);
        Assert.True(rows.IsSuccess, FailMsg(rows));
        return new HashSet<string>(rows.Value.Select(r => r[0]!), StringComparer.Ordinal);
    }

    private static HashSet<string> QueryTextStyles(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `TextStyle` FROM `TextStyle`", 1);
        Assert.True(rows.IsSuccess, FailMsg(rows));
        return new HashSet<string>(rows.Value.Select(r => r[0]!), StringComparer.Ordinal);
    }

    private static string FailMsg<T>(Result<T> result) =>
        result.IsFailure ? result.Error.Message : "";

    private static void CleanupMsi(string msiPath)
    {
        var dir = Path.GetDirectoryName(msiPath);
        var parentDir = dir is not null ? Path.GetDirectoryName(dir) : null;
        if (parentDir is not null && Directory.Exists(parentDir))
        {
            try { Directory.Delete(parentDir, true); }
            catch { /* Best effort cleanup */ }
        }
    }
}
