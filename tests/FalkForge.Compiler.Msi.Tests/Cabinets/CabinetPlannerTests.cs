using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Cabinets;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Cabinets;

[SupportedOSPlatform("windows")]
public sealed class CabinetPlannerTests
{
    [Fact]
    public void Plan_EmptyFileList_ReturnsNoPlans()
    {
        var plans = CabinetPlanner.Plan([], template: null);

        Assert.Empty(plans);
    }

    [Fact]
    public void Plan_NoTemplate_ReturnsSinglePlanWithDefaultCabinetName()
    {
        var files = BuildFiles(3, fileSize: 1024);

        var plans = CabinetPlanner.Plan(files, template: null);

        var plan = Assert.Single(plans);
        Assert.Equal(1, plan.DiskId);
        Assert.Equal(0, plan.FileStartIndex);
        Assert.Equal(3, plan.FileEndIndex);
        Assert.Equal(3, plan.LastSequence);
        Assert.Equal(CabinetBuilder.DefaultCabinetFileName, plan.CabinetFileName);
        Assert.True(plan.Embedded);
    }

    [Fact]
    public void Plan_TemplateBelowLimit_EmitsSingleCabinet()
    {
        var files = BuildFiles(3, fileSize: 1024);
        var template = new MediaTemplateModel
        {
            CabinetTemplate = "data{0}.cab",
            MaximumCabinetSizeInMB = 100,
            EmbedCabinet = true
        };

        var plans = CabinetPlanner.Plan(files, template);

        var plan = Assert.Single(plans);
        Assert.Equal("data1.cab", plan.CabinetFileName);
        Assert.Equal(3, plan.LastSequence);
    }

    [Fact]
    public void Plan_PayloadExceedsMaximum_SplitsSequentiallyAtSizeBoundary()
    {
        // Files: 4 files * 300KB each = 1.2MB. With a 1 MB cap, three files
        // (~900KB) fit in the first cabinet and the fourth spills into a second.
        var files = BuildFiles(4, fileSize: 300 * 1024);
        var template = new MediaTemplateModel
        {
            CabinetTemplate = "data{0}.cab",
            MaximumCabinetSizeInMB = 1,
            EmbedCabinet = true
        };

        var plans = CabinetPlanner.Plan(files, template);

        // 1 MB cap = 1 048 576 bytes, each file 307 200 bytes, so 3 files fit (921 600)
        // but 4 overflow (1 228 800). Expect 2 cabinets: [0..3], [3..4].
        Assert.Equal(2, plans.Count);
        Assert.Equal("data1.cab", plans[0].CabinetFileName);
        Assert.Equal(0, plans[0].FileStartIndex);
        Assert.Equal(3, plans[0].FileEndIndex);
        Assert.Equal(3, plans[0].LastSequence);
        Assert.Equal("data2.cab", plans[1].CabinetFileName);
        Assert.Equal(3, plans[1].FileStartIndex);
        Assert.Equal(4, plans[1].FileEndIndex);
        Assert.Equal(4, plans[1].LastSequence);
    }

    [Fact]
    public void Plan_SingleFileLargerThanMax_StillEmittedAloneToGuaranteeProgress()
    {
        // File larger than the cap would otherwise stall the planner in an infinite loop.
        var files = BuildFiles(1, fileSize: 10 * 1024 * 1024);
        var template = new MediaTemplateModel
        {
            CabinetTemplate = "data{0}.cab",
            MaximumCabinetSizeInMB = 1,
            EmbedCabinet = true
        };

        var plans = CabinetPlanner.Plan(files, template);

        var plan = Assert.Single(plans);
        Assert.Equal(0, plan.FileStartIndex);
        Assert.Equal(1, plan.FileEndIndex);
    }

    [Fact]
    public void Plan_RespectsEmbedCabinetFlag()
    {
        var files = BuildFiles(1, fileSize: 1);
        var template = new MediaTemplateModel
        {
            CabinetTemplate = "data{0}.cab",
            MaximumCabinetSizeInMB = 10,
            EmbedCabinet = false
        };

        var plans = CabinetPlanner.Plan(files, template);

        Assert.False(Assert.Single(plans).Embedded);
    }

    private static IReadOnlyList<ResolvedFile> BuildFiles(int count, long fileSize)
    {
        var files = new List<ResolvedFile>(count);
        for (var i = 0; i < count; i++)
            files.Add(new ResolvedFile
            {
                SourcePath = $"C:/fake/file{i}.bin",
                TargetDirectory = KnownFolder.ProgramFiles / "Test",
                FileName = $"file{i}.bin",
                FileSize = fileSize,
                ComponentId = $"C{i}",
                FileId = $"F{i}"
            });

        return files;
    }
}
