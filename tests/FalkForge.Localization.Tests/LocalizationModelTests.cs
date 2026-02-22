using FalkForge.Localization;
using Xunit;

namespace FalkForge.Localization.Tests;

public sealed class LocalizationModelTests
{
    [Fact]
    public void Strings_DefaultsToEmptyDictionary()
    {
        var model = new LocalizationModel { Culture = "en-US" };

        Assert.Empty(model.Strings);
    }
}
