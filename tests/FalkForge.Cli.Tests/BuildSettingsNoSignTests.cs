using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class BuildSettingsNoSignTests
{
    [Fact]
    public void Defaults_NoSign_IsFalse()
    {
        var settings = new BuildSettings { ProjectPath = "installer.cs" };

        Assert.False(settings.NoSign);
    }

    [Fact]
    public void NoSign_WhenSet_IsTrue()
    {
        var settings = new BuildSettings { ProjectPath = "installer.cs", NoSign = true };

        Assert.True(settings.NoSign);
    }

    [Fact]
    public void Validate_WithNoSign_ReturnsSuccess()
    {
        var settings = new BuildSettings { ProjectPath = "installer.cs", NoSign = true };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }
}
