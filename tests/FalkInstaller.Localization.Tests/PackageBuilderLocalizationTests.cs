using FalkInstaller.Builders;
using FalkInstaller.Testing;
using Xunit;

namespace FalkInstaller.Localization.Tests;

public sealed class PackageBuilderLocalizationTests
{
    [Fact]
    public void Localization_AddsLocalizationDataToPackageModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Localization(loc =>
            {
                loc.AddCulture("en-US", new Dictionary<string, string>
                {
                    ["ProductName"] = "My Application"
                });
                loc.DefaultCulture("en-US");
            });
        });

        Assert.NotNull(package.LocalizationData);
        Assert.Single(package.LocalizationData);
        Assert.Equal("en-US", package.LocalizationData[0].Culture);
        Assert.Equal("My Application", package.LocalizationData[0].Strings["ProductName"]);
    }

    [Fact]
    public void Localization_NotConfigured_LocalizationDataIsEmpty()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        Assert.Empty(package.LocalizationData);
    }

    [Fact]
    public void Localization_MultipleCultures_AllAvailable()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Localization(loc =>
            {
                loc.AddCulture("en-US", new Dictionary<string, string> { ["Hello"] = "Hello" });
                loc.AddCulture("de", new Dictionary<string, string> { ["Hello"] = "Hallo" });
                loc.DefaultCulture("en-US");
            });
        });

        Assert.Equal(2, package.LocalizationData.Count);
    }
}
