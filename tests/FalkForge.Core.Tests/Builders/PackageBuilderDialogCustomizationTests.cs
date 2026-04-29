using System;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class PackageBuilderDialogCustomizationTests
{
    [Fact]
    public void UseDialogSet_with_callback_invokes_configure()
    {
        bool invoked = false;
        PackageBuilder builder = MakeBuilder();

        builder.UseDialogSet(MsiDialogSet.InstallDir, c =>
        {
            invoked = true;
            c.WindowTitle("My App Setup");
        });

        Assert.True(invoked);
    }

    [Fact]
    public void UseDialogSet_with_null_callback_throws()
    {
        PackageBuilder builder = MakeBuilder();

        Assert.Throws<ArgumentNullException>(() =>
            builder.UseDialogSet(MsiDialogSet.InstallDir, configure: null!));
    }

    [Fact]
    public void Build_produces_PackageModel_with_dialog_customization_when_set()
    {
        PackageBuilder builder = MakeBuilder()
            .UseDialogSet(MsiDialogSet.InstallDir, c =>
            {
                c.BannerBitmap("banner.bmp");
                c.WindowTitle("Setup");
                c.OverrideButtonLabel(DialogButton.Next, "Continue");
                c.SuppressDialog(StockDialog.Welcome);
            });

        PackageModel model = builder.Build();

        Assert.NotNull(model.DialogCustomization);
        Assert.Equal("banner.bmp", model.DialogCustomization!.BannerBitmap);
        Assert.Equal("Setup", model.DialogCustomization.WindowTitle);
        Assert.Equal("Continue", model.DialogCustomization.ButtonLabelOverrides[DialogButton.Next]);
        Assert.Contains(StockDialog.Welcome, model.DialogCustomization.SuppressedDialogs);
    }

    [Fact]
    public void Build_produces_PackageModel_with_null_customization_when_not_set()
    {
        PackageModel model = MakeBuilder().UseDialogSet(MsiDialogSet.InstallDir).Build();

        Assert.Null(model.DialogCustomization);
    }

    private static PackageBuilder MakeBuilder()
    {
        return new PackageBuilder
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
        };
    }
}
