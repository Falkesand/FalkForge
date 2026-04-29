using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Models;

public sealed class PackageModelDialogCustomizationTests
{
    [Fact]
    public void Default_DialogCustomization_is_null()
    {
        var model = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new System.Version(1, 0, 0),
        };

        Assert.Null(model.DialogCustomization);
    }

    [Fact]
    public void Init_assigns_DialogCustomization()
    {
        var customization = new DialogCustomizationModel { BannerBitmap = "banner.bmp" };
        var model = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new System.Version(1, 0, 0),
            DialogCustomization = customization,
        };

        Assert.Same(customization, model.DialogCustomization);
        Assert.Equal("banner.bmp", model.DialogCustomization!.BannerBitmap);
    }
}
