using FalkForge.Extensions.Util.ScheduledTask;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.ScheduledTask;

public sealed class ScheduledTaskBuilderTests
{
    [Fact]
    public void Build_WithAllProperties_SetsAllFields()
    {
        var model = new ScheduledTaskBuilder("ST1")
            .Name("My Task")
            .Command("cmd.exe")
            .Arguments("/c script.bat")
            .WorkingDirectory("[INSTALLFOLDER]")
            .TriggerOnLogin()
            .RunAsUser("SYSTEM")
            .RunElevated()
            .Build();

        Assert.Equal("ST1", model.Id);
        Assert.Equal("My Task", model.Name);
        Assert.Equal("cmd.exe", model.Command);
        Assert.Equal("/c script.bat", model.Arguments);
        Assert.Equal("[INSTALLFOLDER]", model.WorkingDirectory);
        Assert.Equal(ScheduledTaskTriggerType.OnLogin, model.TriggerType);
        Assert.Equal("SYSTEM", model.RunAsUser);
        Assert.True(model.RunElevated);
    }

    [Fact]
    public void Build_TriggerOnSchedule_SetsTriggerTypeAndSchedule()
    {
        var model = new ScheduledTaskBuilder("ST2")
            .Name("Scheduled")
            .Command("backup.exe")
            .TriggerOnSchedule("0 3 * * *")
            .Build();

        Assert.Equal(ScheduledTaskTriggerType.OnSchedule, model.TriggerType);
        Assert.Equal("0 3 * * *", model.Schedule);
    }

    [Fact]
    public void Build_DefaultTrigger_IsOnInstall()
    {
        var model = new ScheduledTaskBuilder("ST3")
            .Name("Default Trigger")
            .Command("app.exe")
            .Build();

        Assert.Equal(ScheduledTaskTriggerType.OnInstall, model.TriggerType);
    }
}
