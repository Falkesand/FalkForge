using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI;

[SupportedOSPlatform("windows")]
public sealed class DialogEmitterTests
{
    [Fact]
    public void NoneDialogSet_EmitsNoDialogTable()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.None, "NoneApp");
        try
        {
            using var db = OpenMsi(msiPath);
            // Dialog table should not exist when None is specified
            var result = db.Execute("SELECT * FROM `Dialog`");
            Assert.True(result.IsFailure, "Dialog table should not exist for None dialog set");
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void NoneDialogSet_EmitsNoTextStyleTable()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.None, "NoneTS");
        try
        {
            using var db = OpenMsi(msiPath);
            var result = db.Execute("SELECT * FROM `TextStyle`");
            Assert.True(result.IsFailure, "TextStyle table should not exist for None dialog set");
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void NoneDialogSet_EmitsNoUITextTable()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.None, "NoneUT");
        try
        {
            using var db = OpenMsi(msiPath);
            var result = db.Execute("SELECT * FROM `UIText`");
            Assert.True(result.IsFailure, "UIText table should not exist for None dialog set");
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void DialogTableCreation_Succeeds()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "TblCreate");
        try
        {
            using var db = OpenMsi(msiPath);
            AssertTableExists(db, "Dialog");
            AssertTableExists(db, "Control");
            AssertTableExists(db, "ControlEvent");
            AssertTableExists(db, "TextStyle");
            AssertTableExists(db, "UIText");
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void ControlConditionTable_CreatedForTemplatesWithConditions()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.InstallDir, "CondTbl");
        try
        {
            using var db = OpenMsi(msiPath);
            AssertTableExists(db, "ControlCondition");
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void EmittedDialogData_IsQueryableFromDatabase()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "QueryApp");
        try
        {
            using var db = OpenMsi(msiPath);

            // Query Dialog table with multiple columns
            var dialogRows = db.QueryRows(
                "SELECT `Dialog`, `Width`, `Height`, `Control_First` FROM `Dialog`", 4);
            Assert.True(dialogRows.IsSuccess, FailMsg(dialogRows));
            Assert.True(dialogRows.Value.Count > 0, "Dialog table should have rows");

            // Verify standard dimensions
            foreach (var row in dialogRows.Value)
            {
                Assert.Equal("370", row[1]); // Width
                Assert.Equal("270", row[2]); // Height
            }
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void UITextEntries_ArePresent()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "UITextApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var rows = db.QueryRows("SELECT `Key`, `Text` FROM `UIText`", 2);
            Assert.True(rows.IsSuccess, FailMsg(rows));
            Assert.True(rows.Value.Count > 0, "UIText table should have entries");

            var keys = rows.Value.Select(r => r[0]).ToHashSet();
            Assert.Contains("bytes", keys);
            Assert.Contains("KB", keys);
            Assert.Contains("MB", keys);
            Assert.Contains("GB", keys);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void TextStyleEntries_HaveCorrectFontData()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "FontData");
        try
        {
            using var db = OpenMsi(msiPath);
            var rows = db.QueryRows(
                "SELECT `TextStyle`, `FaceName`, `Size` FROM `TextStyle`", 3);
            Assert.True(rows.IsSuccess, FailMsg(rows));
            Assert.True(rows.Value.Count >= 5, "Should have at least 5 TextStyle entries");

            // Verify DlgFont8 has Tahoma, size 8
            var dlgFont8 = rows.Value.FirstOrDefault(r => r[0] == "DlgFont8");
            Assert.NotNull(dlgFont8);
            Assert.Equal("Tahoma", dlgFont8[1]);
            Assert.Equal("8", dlgFont8[2]);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void DialogTitleProperty_ContainsProductNamePlaceholder()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "TitleApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var rows = db.QueryRows("SELECT `Dialog`, `Title` FROM `Dialog`", 2);
            Assert.True(rows.IsSuccess, FailMsg(rows));

            // All dialogs should have [ProductName] in the title
            foreach (var row in rows.Value)
            {
                Assert.Contains("[ProductName]", row[1]!);
            }
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void ExitDialog_HasFinishButtonWithEndDialogEvent()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "ExitBtnApp");
        try
        {
            using var db = OpenMsi(msiPath);

            // Check that ExitDlg has a Finish button
            var controls = db.QueryRows(
                "SELECT `Dialog_`, `Control`, `Type` FROM `Control`", 3);
            Assert.True(controls.IsSuccess, FailMsg(controls));

            var finishButton = controls.Value.FirstOrDefault(r =>
                r[0] == "ExitDlg" && r[1] == "Finish" && r[2] == "PushButton");
            Assert.NotNull(finishButton);

            // Check EndDialog event
            var events = db.QueryRows(
                "SELECT `Dialog_`, `Control_`, `Event`, `Argument` FROM `ControlEvent`", 4);
            Assert.True(events.IsSuccess, FailMsg(events));

            var endEvent = events.Value.FirstOrDefault(r =>
                r[0] == "ExitDlg" && r[1] == "Finish" && r[2] == "EndDialog" && r[3] == "Return");
            Assert.NotNull(endEvent);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void CancelButtons_TriggerSpawnDialogEvent()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.InstallDir, "CancelApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var events = db.QueryRows(
                "SELECT `Dialog_`, `Control_`, `Event`, `Argument` FROM `ControlEvent`", 4);
            Assert.True(events.IsSuccess, FailMsg(events));

            var cancelEvents = events.Value.Where(r =>
                r[1] == "Cancel" && r[2] == "SpawnDialog" && r[3] == "CancelDlg").ToList();

            // Multiple dialogs should have Cancel -> SpawnDialog(CancelDlg)
            Assert.True(cancelEvents.Count >= 3, $"Expected at least 3 Cancel events, found {cancelEvents.Count}");
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void BackAndNextButtons_UseNewDialogEvent()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.InstallDir, "NavApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var events = db.QueryRows(
                "SELECT `Dialog_`, `Control_`, `Event` FROM `ControlEvent`", 3);
            Assert.True(events.IsSuccess, FailMsg(events));

            var nextEvents = events.Value.Where(r =>
                r[1] == "Next" && r[2] == "NewDialog").ToList();
            var backEvents = events.Value.Where(r =>
                r[1] == "Back" && r[2] == "NewDialog").ToList();

            Assert.True(nextEvents.Count >= 2, $"Expected at least 2 Next/NewDialog events, found {nextEvents.Count}");
            Assert.True(backEvents.Count >= 2, $"Expected at least 2 Back/NewDialog events, found {backEvents.Count}");
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void EventMappingTable_IsCreated()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "EvtMapApp");
        try
        {
            using var db = OpenMsi(msiPath);
            AssertTableExists(db, "EventMapping");
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Fact]
    public void InstallUISequence_HasExpectedActions()
    {
        var msiPath = CompileWithDialogSet(MsiDialogSet.Minimal, "UISeqApp");
        try
        {
            using var db = OpenMsi(msiPath);
            var rows = db.QueryRows(
                "SELECT `Action` FROM `InstallUISequence`", 1);
            Assert.True(rows.IsSuccess, FailMsg(rows));

            var actions = rows.Value.Select(r => r[0]).ToHashSet();
            Assert.Contains("CostInitialize", actions);
            Assert.Contains("CostFinalize", actions);
            Assert.Contains("ExecuteAction", actions);
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.InstallDir)]
    [InlineData(MsiDialogSet.FeatureTree)]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void ControlText_ContainsNoUnresolvedLocPatterns(MsiDialogSet dialogSet)
    {
        var msiPath = CompileWithDialogSet(dialogSet, $"LocRes_{dialogSet}");
        try
        {
            using var db = OpenMsi(msiPath);
            var rows = db.QueryRows(
                "SELECT `Dialog_`, `Control`, `Text` FROM `Control`", 3);
            Assert.True(rows.IsSuccess, FailMsg(rows));

            var unresolved = rows.Value
                .Where(r => r[2] is not null && r[2]!.Contains("!(loc."))
                .Select(r => $"{r[0]}.{r[1]}: {r[2]}")
                .ToList();

            Assert.True(unresolved.Count == 0,
                $"Found {unresolved.Count} controls with unresolved !(loc. patterns:\n" +
                string.Join("\n", unresolved));
        }
        finally
        {
            CleanupMsi(msiPath);
        }
    }

    // --- Helper methods ---

    private static string CompileWithDialogSet(MsiDialogSet dialogSet, string appName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"DlgEmit_{appName}_{Guid.NewGuid():N}");
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

    private static void AssertTableExists(MsiDatabase db, string tableName)
    {
        var result = db.Execute($"SELECT * FROM `{tableName}`");
        Assert.True(result.IsSuccess, $"Table '{tableName}' not found: {(result.IsFailure ? result.Error.Message : "")}");
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
