using FalkForge.Localization;
using Xunit;

namespace FalkForge.Localization.Tests;

public sealed class LocalizationModelTests
{
    [Fact]
    public void Culture_ReturnsAssignedCulture()
    {
        var model = new LocalizationModel
        {
            Culture = "de-AT",
            Strings = new Dictionary<string, string> { ["Hello"] = "Hallo" }
        };

        Assert.Equal("de-AT", model.Culture);
    }

    [Fact]
    public void Strings_ReturnsAssignedDictionary()
    {
        var strings = new Dictionary<string, string>
        {
            ["ProductName"] = "My App",
            ["WelcomeTitle"] = "Welcome"
        };

        var model = new LocalizationModel
        {
            Culture = "en-US",
            Strings = strings
        };

        Assert.Equal(2, model.Strings.Count);
        Assert.Equal("My App", model.Strings["ProductName"]);
        Assert.Equal("Welcome", model.Strings["WelcomeTitle"]);
    }

    [Fact]
    public void Strings_DefaultsToEmptyDictionary()
    {
        var model = new LocalizationModel { Culture = "en-US" };

        Assert.Empty(model.Strings);
    }
}
