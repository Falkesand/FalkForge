using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class PlanCommandTests
{
    [Fact]
    public void PlanSettings_HasProjectPathProperty()
    {
        var settings = new PlanSettings { ProjectPath = "installer.csx" };
        Assert.Equal("installer.csx", settings.ProjectPath);
    }

    [Fact]
    public void PlanSettings_HasOutputPathProperty()
    {
        var settings = new PlanSettings { ProjectPath = "installer.csx", OutputPath = "plan.json" };
        Assert.Equal("plan.json", settings.OutputPath);
    }
}
