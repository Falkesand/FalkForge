using System.Collections.Immutable;
using System.Linq;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogComposerCustomizationTests
{
    private static DialogContent ButtonRowContent(params PlacedControl[] controls) => new()
    {
        Name = "TestDlg",
        Kind = "Welcome",
        TitleLocKey = "OriginalTitle",
        Placements = ImmutableArray.Create(
            new RegionPlacement
            {
                RegionName = "ButtonRow",
                Controls = controls.ToImmutableArray(),
            }),
    };

    private static PlacedControl Button(string name, string text) =>
        new() { Name = name, Type = "PushButton", TextOrLocKey = text };

    [Fact]
    public void Compose_with_null_customization_matches_two_arg_overload()
    {
        var content = ButtonRowContent(Button("Next", "!(loc.Button.Next)"));

        var twoArg = DialogComposer.Compose(content, Layouts.Standard370x270);
        var threeArgNull = DialogComposer.Compose(content, Layouts.Standard370x270, customization: null);

        Assert.Equal(twoArg.Name, threeArgNull.Name);
        Assert.Equal(twoArg.Title, threeArgNull.Title);
        Assert.Equal(twoArg.Controls.Count, threeArgNull.Controls.Count);
        for (int i = 0; i < twoArg.Controls.Count; i++)
        {
            Assert.Equal(twoArg.Controls[i].Name, threeArgNull.Controls[i].Name);
            Assert.Equal(twoArg.Controls[i].Text, threeArgNull.Controls[i].Text);
        }
    }

    [Fact]
    public void Compose_with_button_label_override_replaces_control_text()
    {
        var content = ButtonRowContent(
            Button("Next", "!(loc.Button.Next)"),
            Button("Cancel", "!(loc.Button.Cancel)"));
        var customization = new DialogCustomization()
            .OverrideButtonLabel(DialogButton.Next, "Continue")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var next = model.Controls.Single(c => c.Name == "Next");
        Assert.Equal("Continue", next.Text);
        var cancel = model.Controls.Single(c => c.Name == "Cancel");
        Assert.Equal("!(loc.Button.Cancel)", cancel.Text);
    }

    [Fact]
    public void Compose_with_unknown_button_override_is_silently_ignored()
    {
        var content = ButtonRowContent(Button("Next", "!(loc.Button.Next)"));
        // Print is unmapped to any control in this dialog -> override is no-op.
        var customization = new DialogCustomization()
            .OverrideButtonLabel(DialogButton.Print, "Print Me")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var next = model.Controls.Single(c => c.Name == "Next");
        Assert.Equal("!(loc.Button.Next)", next.Text);
    }

    [Fact]
    public void Compose_with_browse_button_override_targets_ChangeFolder_control()
    {
        var content = ButtonRowContent(Button("ChangeFolder", "!(loc.Button.Browse)"));
        var customization = new DialogCustomization()
            .OverrideButtonLabel(DialogButton.Browse, "Pick Folder")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var changeFolder = model.Controls.Single(c => c.Name == "ChangeFolder");
        Assert.Equal("Pick Folder", changeFolder.Text);
    }

    [Fact]
    public void Compose_with_banner_bitmap_swap_replaces_banner_control_text()
    {
        var content = new DialogContent
        {
            Name = "BannerDlg",
            Kind = "Welcome",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ContentArea",
                    Controls = ImmutableArray.Create(
                        new PlacedControl
                        {
                            Name = "BannerBmp",
                            Type = "Bitmap",
                            TextOrLocKey = "OriginalBanner.bmp",
                        }),
                }),
        };
        var customization = new DialogCustomization()
            .BannerBitmap("C:/branding/banner.bmp")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var banner = model.Controls.Single(c => c.Name == "BannerBmp");
        Assert.Equal("C:/branding/banner.bmp", banner.Text);
    }

    [Fact]
    public void Compose_with_header_icon_swap_replaces_icon_control_text()
    {
        var content = new DialogContent
        {
            Name = "IconDlg",
            Kind = "Welcome",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ContentArea",
                    Controls = ImmutableArray.Create(
                        new PlacedControl
                        {
                            Name = "HeaderIcon",
                            Type = "Icon",
                            TextOrLocKey = "OriginalIcon.ico",
                        }),
                }),
        };
        var customization = new DialogCustomization()
            .HeaderIcon("C:/branding/icon.ico")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var icon = model.Controls.Single(c => c.Name == "HeaderIcon");
        Assert.Equal("C:/branding/icon.ico", icon.Text);
    }

    [Fact]
    public void Compose_with_window_title_sets_model_title()
    {
        var content = ButtonRowContent(Button("Next", "next"));
        var customization = new DialogCustomization()
            .WindowTitle("My Custom Title")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        Assert.Equal("My Custom Title", model.Title);
    }

    [Fact]
    public void Compose_with_multiple_overrides_applies_all()
    {
        var content = new DialogContent
        {
            Name = "MultiDlg",
            Kind = "Welcome",
            TitleLocKey = "Original",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ContentArea",
                    Controls = ImmutableArray.Create(
                        new PlacedControl { Name = "BannerBmp", Type = "Bitmap", TextOrLocKey = "old.bmp" },
                        new PlacedControl { Name = "HeaderIcon", Type = "Icon", TextOrLocKey = "old.ico" }),
                },
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(
                        new PlacedControl { Name = "Next", Type = "PushButton", TextOrLocKey = "next-old" },
                        new PlacedControl { Name = "Cancel", Type = "PushButton", TextOrLocKey = "cancel-old" }),
                }),
        };
        var customization = new DialogCustomization()
            .WindowTitle("New Title")
            .BannerBitmap("new.bmp")
            .HeaderIcon("new.ico")
            .OverrideButtonLabel(DialogButton.Next, "Go")
            .OverrideButtonLabel(DialogButton.Cancel, "Stop")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        Assert.Equal("New Title", model.Title);
        Assert.Equal("new.bmp", model.Controls.Single(c => c.Name == "BannerBmp").Text);
        Assert.Equal("new.ico", model.Controls.Single(c => c.Name == "HeaderIcon").Text);
        Assert.Equal("Go", model.Controls.Single(c => c.Name == "Next").Text);
        Assert.Equal("Stop", model.Controls.Single(c => c.Name == "Cancel").Text);
    }

    [Fact]
    public void Compose_with_no_overrides_leaves_controls_unchanged()
    {
        var content = ButtonRowContent(
            Button("Next", "!(loc.Button.Next)"),
            Button("Cancel", "!(loc.Button.Cancel)"));
        var customization = new DialogCustomization().ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        Assert.Equal("!(loc.Button.Next)", model.Controls.Single(c => c.Name == "Next").Text);
        Assert.Equal("!(loc.Button.Cancel)", model.Controls.Single(c => c.Name == "Cancel").Text);
        Assert.Equal("OriginalTitle", model.Title);
    }
}
