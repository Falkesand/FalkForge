using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class InspectSettingsTests
{
    [Fact]
    public void Validate_ValidMsiPath_ReturnsSuccess()
    {
        var settings = new InspectSettings { MsiPath = "package.msi" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_EmptyMsiPath_ReturnsError()
    {
        var settings = new InspectSettings { MsiPath = "" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_NonMsiExtension_ReturnsError()
    {
        var settings = new InspectSettings { MsiPath = "file.exe" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_CaseInsensitiveMsiExtension_ReturnsSuccess()
    {
        var settings = new InspectSettings { MsiPath = "Package.MSI" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }
}
