using System.Text.Json;
using FalkForge.Cli.Diff;
using Xunit;

namespace FalkForge.Cli.Tests.Diff;

/// <summary>
/// Tests for <see cref="PlanDiffRenderer"/>. Verifies Markdown, JSON, and Spectre output
/// shapes without asserting exact whitespace — stable against minor formatting tweaks.
/// </summary>
public sealed class PlanDiffRendererTests
{
    // -------------------------------------------------------------------------
    // Fixture
    // -------------------------------------------------------------------------
    private static PlanDiffResult MakeResult(bool hasChanges = true)
    {
        if (!hasChanges)
            return new PlanDiffResult("msi", "a.msi", "b.msi", []);

        var items = new List<DiffItem>
        {
            new(DiffStatus.Added,   "ServiceA", null,       "type=16, start=2"),
            new(DiffStatus.Removed, "ServiceB", "type=16, start=3", null),
            new(DiffStatus.Changed, "Version",  "1.0.0",   "2.0.0"),
        };
        var section = new PlanDiffSection("Services", items);
        return new PlanDiffResult("msi", "old.msi", "new.msi", [section]);
    }

    // -------------------------------------------------------------------------
    // Markdown
    // -------------------------------------------------------------------------
    [Fact]
    public void RenderMarkdown_NoChanges_EmitsNoChangesText()
    {
        var result = MakeResult(hasChanges: false);
        var md = PlanDiffRenderer.RenderMarkdown(result);

        Assert.Contains("No differences found", md);
        Assert.DoesNotContain("###", md);
    }

    [Fact]
    public void RenderMarkdown_HasChanges_EmitsH2AndH3Headers()
    {
        var md = PlanDiffRenderer.RenderMarkdown(MakeResult());

        Assert.Contains("## MSI plan diff", md);
        Assert.Contains("### Services", md);
    }

    [Fact]
    public void RenderMarkdown_AddedItem_UsesPlus()
    {
        var md = PlanDiffRenderer.RenderMarkdown(MakeResult());
        Assert.Contains("+ **ServiceA**", md);
    }

    [Fact]
    public void RenderMarkdown_RemovedItem_UsesMinus()
    {
        var md = PlanDiffRenderer.RenderMarkdown(MakeResult());
        Assert.Contains("- **ServiceB**", md);
    }

    [Fact]
    public void RenderMarkdown_ChangedItem_UsesTilde()
    {
        var md = PlanDiffRenderer.RenderMarkdown(MakeResult());
        Assert.Contains("~ **Version**", md);
        Assert.Contains("was:", md);
        Assert.Contains("now:", md);
    }

    [Fact]
    public void RenderMarkdown_Paths_Included()
    {
        var md = PlanDiffRenderer.RenderMarkdown(MakeResult());
        Assert.Contains("old.msi", md);
        Assert.Contains("new.msi", md);
    }

    // -------------------------------------------------------------------------
    // JSON
    // -------------------------------------------------------------------------
    [Fact]
    public void RenderJson_ValidJson_Deserializes()
    {
        var json = PlanDiffRenderer.RenderJson(MakeResult());
        using var doc = JsonDocument.Parse(json); // must not throw
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("version", out _));
        Assert.True(root.TryGetProperty("mode", out _));
        Assert.True(root.TryGetProperty("hasChanges", out _));
        Assert.True(root.TryGetProperty("totalChanges", out _));
        Assert.True(root.TryGetProperty("sections", out _));
    }

    [Fact]
    public void RenderJson_Version_IsCurrentVersion()
    {
        var json = PlanDiffRenderer.RenderJson(MakeResult());
        using var doc = JsonDocument.Parse(json);
        var version = doc.RootElement.GetProperty("version").GetInt32();
        Assert.Equal(PlanDiffJsonEnvelope.CurrentVersion, version);
    }

    [Fact]
    public void RenderJson_HasChanges_True()
    {
        var json = PlanDiffRenderer.RenderJson(MakeResult(hasChanges: true));
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("hasChanges").GetBoolean());
    }

    [Fact]
    public void RenderJson_NoChanges_HasChangesFalse()
    {
        var json = PlanDiffRenderer.RenderJson(MakeResult(hasChanges: false));
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("hasChanges").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("totalChanges").GetInt32());
    }

    [Fact]
    public void RenderJson_ItemStatuses_LowerCase()
    {
        var json = PlanDiffRenderer.RenderJson(MakeResult());
        using var doc = JsonDocument.Parse(json);
        var sections = doc.RootElement.GetProperty("sections");
        var firstSection = sections.EnumerateArray().First();
        var items = firstSection.GetProperty("items").EnumerateArray().ToList();

        // All status values must be lowercase
        foreach (var item in items)
        {
            var status = item.GetProperty("status").GetString()!;
            Assert.Equal(status.ToLowerInvariant(), status);
        }
    }

    [Fact]
    public void RenderJson_Paths_InEnvelope()
    {
        var json = PlanDiffRenderer.RenderJson(MakeResult());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("old.msi", doc.RootElement.GetProperty("oldPath").GetString());
        Assert.Equal("new.msi", doc.RootElement.GetProperty("newPath").GetString());
    }

    [Fact]
    public void RenderJson_Mode_InEnvelope()
    {
        var json = PlanDiffRenderer.RenderJson(MakeResult());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("msi", doc.RootElement.GetProperty("mode").GetString());
    }

    // -------------------------------------------------------------------------
    // Spectre (verifies it writes to IConsoleOutput without throwing)
    // -------------------------------------------------------------------------
    [Fact]
    public void RenderSpectre_NoChanges_WritesNoChangesLine()
    {
        var output = new TestConsoleOutput();
        PlanDiffRenderer.RenderSpectre(MakeResult(hasChanges: false), output);
        Assert.Contains(output.MarkupLines, l => l.Contains("No differences found"));
    }

    [Fact]
    public void RenderSpectre_HasChanges_WritesChangeSummaryAndItems()
    {
        var output = new TestConsoleOutput();
        PlanDiffRenderer.RenderSpectre(MakeResult(), output);

        var all = output.MarkupLines.Concat(output.Lines).ToList();
        Assert.Contains(all, l => l.Contains("change"));
    }

    [Fact]
    public void RenderSpectre_NullValues_DoNotThrow()
    {
        // Verifies Added/Removed items with null old/new values render gracefully.
        var output = new TestConsoleOutput();
        var items = new List<DiffItem>
        {
            new(DiffStatus.Added,   "NewPkg", null,    "type=Msi"),
            new(DiffStatus.Removed, "OldPkg", "type=Msi", null),
        };
        var result = new PlanDiffResult("bundle", "a.exe", "b.exe",
            [new PlanDiffSection("Packages", items)]);

        var ex = Record.Exception(() => PlanDiffRenderer.RenderSpectre(result, output));
        Assert.Null(ex);
    }
}
