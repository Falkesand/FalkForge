using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class SequenceSchedulingTests
{
    [Fact]
    public void CustomAction_AfterStandardAction_GetsCorrectSequenceNumber()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.ExecuteSequence(s => s
                .Action("CA_AfterInstallFiles")
                .After("InstallFiles"));
        });

        var rows = QueryExecuteSequenceRows(ctx.Database);
        var installFilesSeq = GetSequenceNumber(rows, "InstallFiles");
        var customSeq = GetSequenceNumber(rows, "CA_AfterInstallFiles");

        Assert.True(customSeq > installFilesSeq,
            $"Expected CA_AfterInstallFiles ({customSeq}) > InstallFiles ({installFilesSeq})");
    }

    [Fact]
    public void CustomAction_BeforeStandardAction_GetsCorrectSequenceNumber()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.ExecuteSequence(s => s
                .Action("CA_BeforeFinalize")
                .Before("InstallFinalize"));
        });

        var rows = QueryExecuteSequenceRows(ctx.Database);
        var finalizeSeq = GetSequenceNumber(rows, "InstallFinalize");
        var customSeq = GetSequenceNumber(rows, "CA_BeforeFinalize");

        Assert.True(customSeq < finalizeSeq,
            $"Expected CA_BeforeFinalize ({customSeq}) < InstallFinalize ({finalizeSeq})");
    }

    [Fact]
    public void CustomAction_AtExplicitPosition_InsertedCorrectly()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.ExecuteSequence(s => s
                .Action("CA_Explicit")
                .At(5555));
        });

        var rows = QueryExecuteSequenceRows(ctx.Database);
        var seq = GetSequenceNumber(rows, "CA_Explicit");
        Assert.Equal(5555, seq);
    }

    [Fact]
    public void MultipleCustomActions_OrderedCorrectly()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.ExecuteSequence(s => s
                .Action("CA_First")
                .After("InstallFiles")
                .Action("CA_Second")
                .After("CA_First")
                .Action("CA_Third")
                .Before("InstallFinalize"));
        });

        var rows = QueryExecuteSequenceRows(ctx.Database);
        var firstSeq = GetSequenceNumber(rows, "CA_First");
        var secondSeq = GetSequenceNumber(rows, "CA_Second");
        var thirdSeq = GetSequenceNumber(rows, "CA_Third");
        var finalizeSeq = GetSequenceNumber(rows, "InstallFinalize");

        Assert.True(firstSeq < secondSeq,
            $"Expected CA_First ({firstSeq}) < CA_Second ({secondSeq})");
        Assert.True(thirdSeq < finalizeSeq,
            $"Expected CA_Third ({thirdSeq}) < InstallFinalize ({finalizeSeq})");
    }

    [Fact]
    public void Condition_EmittedInSequenceTable()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.ExecuteSequence(s => s
                .Action("CA_Conditional")
                .After("InstallFiles")
                .Condition("NOT Installed"));
        });

        var rows = QueryExecuteSequenceRowsFull(ctx.Database);
        var row = rows.First(r => r.Action == "CA_Conditional");
        Assert.Equal("NOT Installed", row.Condition);
    }

    [Fact]
    public void UISequence_EmittedWhenConfigured()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.UISequence(s => s
                .Action("CA_UIAction")
                .After("CostFinalize"));
        });

        var uiRows = QueryUISequenceRows(ctx.Database);
        Assert.Contains("CA_UIAction", uiRows.Select(r => r.Action));
    }

    [Fact]
    public void NoDuplicateSequenceNumbers_InExecuteSequence()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.ExecuteSequence(s => s
                .Action("CA_At4000")
                .At(4000)
                .Action("CA_AlsoAt4000")
                .At(4000));
        });

        var rows = QueryExecuteSequenceRows(ctx.Database);
        var sequences = rows.Values.ToList();
        var distinct = sequences.Distinct().ToList();
        Assert.Equal(distinct.Count, sequences.Count);
    }

    [Fact]
    public void StandardActions_StillEmittedCorrectly_WhenCustomActionsAdded()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.ExecuteSequence(s => s
                .Action("CA_Custom")
                .After("InstallFiles"));
        });

        var rows = QueryExecuteSequenceRows(ctx.Database);

        // Verify standard actions are still present
        Assert.True(rows.ContainsKey("AppSearch"));
        Assert.True(rows.ContainsKey("LaunchConditions"));
        Assert.True(rows.ContainsKey("CostInitialize"));
        Assert.True(rows.ContainsKey("InstallInitialize"));
        Assert.True(rows.ContainsKey("InstallFiles"));
        Assert.True(rows.ContainsKey("InstallFinalize"));
    }

    [Fact]
    public void NoUISequenceTable_WhenNotConfigured()
    {
        using var ctx = CompileWithSequence(_ => { });

        // Query the UI sequence table - should have no custom rows
        // The standard UI actions are only emitted when UISequenceActions is non-empty
        var uiRows = QueryUISequenceRows(ctx.Database);
        Assert.Empty(uiRows);
    }

    [Fact]
    public void UISequence_ContainsStandardActions_WhenConfigured()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.UISequence(s => s
                .Action("CA_UICustom")
                .After("CostFinalize"));
        });

        var uiRows = QueryUISequenceRows(ctx.Database);
        var actionNames = uiRows.Select(r => r.Action).ToHashSet();

        Assert.Contains("CostInitialize", actionNames);
        Assert.Contains("CostFinalize", actionNames);
        Assert.Contains("CA_UICustom", actionNames);
    }

    // --- Inline custom-action scheduling (.After/.Before/.Sequence/.Condition) ---

    [Fact]
    public void InlineCustomAction_After_SchedulesAfterReferencedAction()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.CustomAction("CA_InlineAfter", ca =>
            {
                ca.SetProperty("INLINE_PROP", "1");
                ca.After = "InstallFiles";
            });
        });

        var rows = QueryExecuteSequenceRows(ctx.Database);
        Assert.True(rows.ContainsKey("CA_InlineAfter"),
            "Inline-scheduled custom action (.After) was never emitted into InstallExecuteSequence.");
        Assert.True(rows["CA_InlineAfter"] > rows["InstallFiles"],
            $"Expected CA_InlineAfter ({rows.GetValueOrDefault("CA_InlineAfter")}) > InstallFiles ({rows["InstallFiles"]}).");
    }

    [Fact]
    public void InlineCustomAction_Before_SchedulesBeforeReferencedAction()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.CustomAction("CA_InlineBefore", ca =>
            {
                ca.SetProperty("INLINE_PROP", "1");
                ca.Before = "InstallFinalize";
            });
        });

        var rows = QueryExecuteSequenceRows(ctx.Database);
        Assert.True(rows.ContainsKey("CA_InlineBefore"),
            "Inline-scheduled custom action (.Before) was never emitted into InstallExecuteSequence.");
        Assert.True(rows["CA_InlineBefore"] < rows["InstallFinalize"],
            $"Expected CA_InlineBefore ({rows.GetValueOrDefault("CA_InlineBefore")}) < InstallFinalize ({rows["InstallFinalize"]}).");
    }

    [Fact]
    public void InlineCustomAction_AbsoluteSequence_UsesExactNumber()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.CustomAction("CA_InlineSeq", ca =>
            {
                ca.SetProperty("INLINE_PROP", "1");
                ca.Sequence = 5555;
            });
        });

        var rows = QueryExecuteSequenceRows(ctx.Database);
        Assert.True(rows.ContainsKey("CA_InlineSeq"),
            "Inline-scheduled custom action (.Sequence) was never emitted into InstallExecuteSequence.");
        Assert.Equal(5555, rows["CA_InlineSeq"]);
    }

    [Fact]
    public void InlineCustomAction_Condition_EmittedOnSequenceRow()
    {
        using var ctx = CompileWithSequence(p =>
        {
            p.CustomAction("CA_InlineCond", ca =>
            {
                ca.SetProperty("INLINE_PROP", "1");
                ca.After = "InstallFiles";
                ca.Condition = "NOT Installed";
            });
        });

        var rows = QueryExecuteSequenceRowsFull(ctx.Database);
        var row = rows.First(r => r.Action == "CA_InlineCond");
        Assert.Equal("NOT Installed", row.Condition);
    }

    [Fact]
    public void CustomAction_ScheduledInlineAndViaExecuteSequence_ProducesSingleRow_ExplicitWins()
    {
        // A custom action pinned BOTH inline (.After) AND via ExecuteSequence(...) for the
        // same Id must yield exactly ONE InstallExecuteSequence row — the explicit entry is
        // authoritative, so no double-insert (which would violate the Action primary key).
        using var ctx = CompileWithSequence(p =>
        {
            p.CustomAction("CA_Both", ca =>
            {
                ca.SetProperty("INLINE_PROP", "1");
                ca.After = "InstallFiles";
            });
            p.ExecuteSequence(s => s
                .Action("CA_Both")
                .At(5555));
        });

        var rows = QueryExecuteSequenceRowsFull(ctx.Database);
        var both = rows.Where(r => r.Action == "CA_Both").ToList();
        Assert.Single(both);
        Assert.Equal(5555, both[0].Sequence);
    }

    // --- Helpers ---

    private sealed class CompilationContext : IDisposable
    {
        public required MsiDatabase Database { get; init; }
        public required string TempDir { get; init; }

        public void Dispose()
        {
            Database.Dispose();
            if (Directory.Exists(TempDir))
                Directory.Delete(TempDir, true);
        }
    }

    private static CompilationContext CompileWithSequence(Action<Builders.PackageBuilder> additionalConfig)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"SeqSch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var sourceDir = Path.Combine(tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, "fake content for sequence scheduling test");

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "SeqTestApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "SeqTestApp"));
            additionalConfig(p);
        });

        var fileSystem = new WindowsFileSystem();
        var compiler = new MsiCompiler(fileSystem);
        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

        return new CompilationContext
        {
            Database = dbResult.Value,
            TempDir = tempDir
        };
    }

    private static Dictionary<string, int> QueryExecuteSequenceRows(MsiDatabase db)
    {
        var rows = db.QueryRows(
            "SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`", 2);
        Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows.Value)
        {
            if (row[0] is { } action && int.TryParse(row[1], out var seq))
                dict[action] = seq;
        }
        return dict;
    }

    private static List<(string Action, string Condition)> QueryUISequenceRows(MsiDatabase db)
    {
        var rows = db.QueryRows(
            "SELECT `Action`, `Condition` FROM `InstallUISequence`", 2);
        Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

        return rows.Value.Select(r => (Action: r[0] ?? "", Condition: r[1] ?? "")).ToList();
    }

    private static List<(string Action, string Condition, int Sequence)> QueryExecuteSequenceRowsFull(MsiDatabase db)
    {
        var rows = db.QueryRows(
            "SELECT `Action`, `Condition`, `Sequence` FROM `InstallExecuteSequence`", 3);
        Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

        return rows.Value.Select(r => (
            Action: r[0] ?? "",
            Condition: r[1] ?? "",
            Sequence: int.TryParse(r[2], out var s) ? s : 0
        )).ToList();
    }

    private static int GetSequenceNumber(Dictionary<string, int> rows, string actionName)
    {
        Assert.True(rows.ContainsKey(actionName), $"Action '{actionName}' not found in sequence table");
        return rows[actionName];
    }
}
