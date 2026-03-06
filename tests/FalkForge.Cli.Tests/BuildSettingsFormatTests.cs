using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class BuildSettingsFormatTests
{
    [Fact]
    public void Validate_FormatMsix_Succeeds()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "installer.cs",
            Format = "msix"
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_FormatInvalid_Fails()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "installer.cs",
            Format = "xyz"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("Invalid format", result.Message);
    }

    [Fact]
    public void Validate_FormatNull_Succeeds()
    {
        var settings = new BuildSettings
        {
            ProjectPath = "installer.cs",
            Format = null
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }
}
