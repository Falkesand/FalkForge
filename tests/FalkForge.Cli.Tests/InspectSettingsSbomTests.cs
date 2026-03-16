using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class InspectSettingsSbomTests
{
    [Fact]
    public void Defaults_ExtractSbom_IsFalse()
    {
        var settings = new InspectSettings { MsiPath = "package.msi" };

        Assert.False(settings.ExtractSbom);
    }

    [Fact]
    public void ExtractSbom_WhenSet_IsTrue()
    {
        var settings = new InspectSettings { MsiPath = "package.msi", ExtractSbom = true };

        Assert.True(settings.ExtractSbom);
    }

    [Fact]
    public void Validate_WithExtractSbom_ReturnsSuccess()
    {
        var settings = new InspectSettings { MsiPath = "package.msi", ExtractSbom = true };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }
}
