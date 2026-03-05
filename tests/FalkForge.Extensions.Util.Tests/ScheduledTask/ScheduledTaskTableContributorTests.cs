using FalkForge.Extensibility;
using FalkForge.Extensions.Util.ScheduledTask;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.ScheduledTask;

public sealed class ScheduledTaskTableContributorTests
{
    private static ExtensionContext CreateContext() => new()
    {
        Package = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid()
        },
        OutputDirectory = "out",
        SourceDirectory = "src"
    };

    [Fact]
    public void GetRows_SingleTask_ReturnsCorrectRow()
    {
        var contributor = new ScheduledTaskTableContributor();
        contributor.Add(new ScheduledTaskModel
        {
            Id = "ST1",
            Name = "Cleanup Task",
            Command = "cmd.exe",
            Arguments = "/c cleanup.bat",
            WorkingDirectory = "[INSTALLFOLDER]",
            TriggerType = ScheduledTaskTriggerType.OnInstall,
            Schedule = null,
            RunAsUser = "SYSTEM",
            RunElevated = true
        });

        var rows = contributor.GetRows(CreateContext());

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal("ST1", row.Get("Id"));
        Assert.Equal("Cleanup Task", row.Get("Name"));
        Assert.Equal("cmd.exe", row.Get("Command"));
        Assert.Equal("/c cleanup.bat", row.Get("Arguments"));
        Assert.Equal("[INSTALLFOLDER]", row.Get("WorkingDirectory"));
        Assert.Equal((int)ScheduledTaskTriggerType.OnInstall, row.Get("TriggerType"));
        Assert.Null(row.Get("Schedule"));
        Assert.Equal("SYSTEM", row.Get("RunAsUser"));
        Assert.Equal(1, row.Get("RunElevated"));
    }

    [Fact]
    public void GetRows_WithSchedule_IncludesScheduleColumn()
    {
        var contributor = new ScheduledTaskTableContributor();
        contributor.Add(new ScheduledTaskModel
        {
            Id = "ST2",
            Name = "Backup Task",
            Command = "backup.exe",
            TriggerType = ScheduledTaskTriggerType.OnSchedule,
            Schedule = "0 2 * * *"
        });

        var rows = contributor.GetRows(CreateContext());

        Assert.Single(rows);
        Assert.Equal("0 2 * * *", rows[0].Get("Schedule"));
        Assert.Equal((int)ScheduledTaskTriggerType.OnSchedule, rows[0].Get("TriggerType"));
    }

    [Fact]
    public void GetRows_Empty_ReturnsNoRows()
    {
        var contributor = new ScheduledTaskTableContributor();

        var rows = contributor.GetRows(CreateContext());

        Assert.Empty(rows);
    }
}
