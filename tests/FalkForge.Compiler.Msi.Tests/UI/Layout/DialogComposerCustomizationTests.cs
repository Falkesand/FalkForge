using System.Collections.Immutable;
using System.Linq;
using FalkForge.Compiler.Msi.UI;
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

    [Fact]
    public void Compose_with_dialog_bitmap_on_welcome_dialog_inserts_background_bitmap_control()
    {
        // Kind = "Welcome" via the ButtonRowContent helper — DialogBitmap targets exterior
        // Welcome/Exit dialogs with a full-canvas background Bitmap control, matching the
        // classic 370x234 WixUI_Bmp_Dialog convention (canvas 370x270 minus the 36 DLU
        // button-row strip at Y=234, per Layouts.Standard370x270's BottomLine region).
        var content = ButtonRowContent(Button("Next", "!(loc.Button.Next)"));
        var customization = new DialogCustomization()
            .DialogBitmap("C:/branding/dialog.bmp")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var bitmap = model.Controls.Single(c => c.Type == MsiControlType.Bitmap);
        Assert.Equal("C:/branding/dialog.bmp", bitmap.Text);
        Assert.Equal(0, bitmap.X);
        Assert.Equal(0, bitmap.Y);
        Assert.Equal(370, bitmap.Width);
        Assert.Equal(234, bitmap.Height);
        // Inserted first so title/description/buttons draw in front of it (MSI Z-orders
        // controls by Control-table row order).
        Assert.Equal(bitmap, model.Controls[0]);
    }

    [Fact]
    public void Compose_with_dialog_bitmap_on_exit_dialog_inserts_background_bitmap_control()
    {
        var content = new DialogContent
        {
            Name = "ExitDlg",
            Kind = "Exit",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(Button("Finish", "!(loc.Button.Finish)")),
                }),
        };
        var customization = new DialogCustomization()
            .DialogBitmap("C:/branding/dialog.bmp")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var bitmap = model.Controls.Single(c => c.Type == MsiControlType.Bitmap);
        Assert.Equal("C:/branding/dialog.bmp", bitmap.Text);
    }

    [Fact]
    public void Compose_with_dialog_bitmap_on_interior_dialog_kind_is_not_applied()
    {
        var content = new DialogContent
        {
            Name = "LicenseAgreementDlg",
            Kind = "License",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(Button("Next", "!(loc.Button.Next)")),
                }),
        };
        var customization = new DialogCustomization()
            .DialogBitmap("C:/branding/dialog.bmp")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        Assert.DoesNotContain(model.Controls, c => c.Type == MsiControlType.Bitmap);
    }

    [Fact]
    public void Compose_with_no_dialog_bitmap_leaves_welcome_dialog_without_background_control()
    {
        var content = ButtonRowContent(Button("Next", "!(loc.Button.Next)"));
        var customization = new DialogCustomization().ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        Assert.DoesNotContain(model.Controls, c => c.Type == MsiControlType.Bitmap);
    }

    [Fact]
    public void Compose_with_dialog_bitmap_and_banner_bitmap_together_does_not_collide()
    {
        var content = new DialogContent
        {
            Name = "WelcomeDlg",
            Kind = "Welcome",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ContentArea",
                    Controls = ImmutableArray.Create(
                        new PlacedControl { Name = "BannerBmp", Type = "Bitmap", TextOrLocKey = "old-banner.bmp" }),
                },
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(Button("Next", "!(loc.Button.Next)")),
                }),
        };
        var customization = new DialogCustomization()
            .BannerBitmap("new-banner.bmp")
            .DialogBitmap("new-dialog.bmp")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var banner = model.Controls.Single(c => c.Name == "BannerBmp");
        Assert.Equal("new-banner.bmp", banner.Text);
        var background = model.Controls.Single(c => c.Type == MsiControlType.Bitmap && c.Name != "BannerBmp");
        Assert.Equal("new-dialog.bmp", background.Text);
    }

    // Interior wizard-page content mirroring LicenseDlgBuilder's shape: a TitleRow + ButtonRow,
    // the structural marker (TitleRow) that distinguishes a full wizard page from a small modal
    // (Cancel/Browse, which never place a TitleRow control).
    private static DialogContent InteriorWizardContent(string kind) => new()
    {
        Name = kind + "Dlg",
        Kind = kind,
        Placements = ImmutableArray.Create(
            new RegionPlacement
            {
                RegionName = "TitleRow",
                Controls = ImmutableArray.Create(
                    new PlacedControl { Name = "Title", Type = "Text", TextOrLocKey = "Title" }),
            },
            new RegionPlacement
            {
                RegionName = "ButtonRow",
                Controls = ImmutableArray.Create(Button("Next", "!(loc.Button.Next)")),
            }),
    };

    [Fact]
    public void Compose_with_banner_bitmap_on_interior_dialog_without_existing_bitmap_synthesizes_banner_control()
    {
        var content = InteriorWizardContent("License");
        var customization = new DialogCustomization()
            .BannerBitmap("C:/branding/banner.bmp")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var banner = model.Controls.Single(c => c.Type == MsiControlType.Bitmap);
        Assert.Equal("C:/branding/banner.bmp", banner.Text);
        Assert.Equal(0, banner.X);
        Assert.Equal(0, banner.Y);
        Assert.Equal(370, banner.Width);
        Assert.Equal(58, banner.Height);
        // Inserted first so Title (TitleRow) draws in front of the banner strip.
        Assert.Equal(banner, model.Controls[0]);
    }

    [Fact]
    public void Compose_with_banner_bitmap_on_dialog_without_title_row_is_not_synthesized()
    {
        // Mirrors CancelDlgBuilder/BrowseDlgBuilder's shape: ContentArea + ButtonRow only, no
        // TitleRow — small modals never get a banner strip.
        var content = new DialogContent
        {
            Name = "CancelDlg",
            Kind = "Cancel",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(Button("No", "!(loc.Button.No)")),
                }),
        };
        var customization = new DialogCustomization()
            .BannerBitmap("C:/branding/banner.bmp")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        Assert.DoesNotContain(model.Controls, c => c.Type == MsiControlType.Bitmap);
    }

    [Fact]
    public void Compose_with_banner_bitmap_on_welcome_dialog_is_not_synthesized()
    {
        var content = InteriorWizardContent("Welcome");
        var customization = new DialogCustomization()
            .BannerBitmap("C:/branding/banner.bmp")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        Assert.DoesNotContain(model.Controls, c => c.Type == MsiControlType.Bitmap);
    }

    [Fact]
    public void Compose_with_header_icon_on_interior_dialog_without_existing_icon_synthesizes_icon_control()
    {
        var content = InteriorWizardContent("InstallDir");
        var customization = new DialogCustomization()
            .HeaderIcon("C:/branding/icon.ico")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var icon = model.Controls.Single(c => c.Type == MsiControlType.Icon);
        Assert.Equal("C:/branding/icon.ico", icon.Text);
        // Top-right of the 370-wide Banner region, vertically aligned with TitleRow (Y=6).
        Assert.Equal(346, icon.X);
        Assert.Equal(6, icon.Y);
        Assert.Equal(16, icon.Width);
        Assert.Equal(16, icon.Height);
    }

    [Fact]
    public void Compose_with_header_icon_on_exit_dialog_is_not_synthesized()
    {
        var content = InteriorWizardContent("Exit");
        var customization = new DialogCustomization()
            .HeaderIcon("C:/branding/icon.ico")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        Assert.DoesNotContain(model.Controls, c => c.Type == MsiControlType.Icon);
    }

    [Fact]
    public void Compose_with_banner_bitmap_and_header_icon_together_orders_banner_behind_icon()
    {
        var content = InteriorWizardContent("SetupType");
        var customization = new DialogCustomization()
            .BannerBitmap("C:/branding/banner.bmp")
            .HeaderIcon("C:/branding/icon.ico")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        Assert.Equal(MsiControlType.Bitmap, model.Controls[0].Type);
        Assert.Equal(MsiControlType.Icon, model.Controls[1].Type);
        Assert.Equal("C:/branding/banner.bmp", model.Controls[0].Text);
        Assert.Equal("C:/branding/icon.ico", model.Controls[1].Text);
    }

    [Fact]
    public void Compose_with_all_three_bitmap_verbs_on_interior_dialog_synthesizes_only_banner_and_icon()
    {
        var content = InteriorWizardContent("Customize");
        var customization = new DialogCustomization()
            .DialogBitmap("C:/branding/dialog.bmp")
            .BannerBitmap("C:/branding/banner.bmp")
            .HeaderIcon("C:/branding/icon.ico")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        Assert.DoesNotContain(model.Controls, c => c.Name == "DialogBmp");
        Assert.Contains(model.Controls, c => c.Type == MsiControlType.Bitmap && c.Text == "C:/branding/banner.bmp");
        Assert.Contains(model.Controls, c => c.Type == MsiControlType.Icon && c.Text == "C:/branding/icon.ico");
    }

    [Fact]
    public void Compose_with_all_three_bitmap_verbs_on_exterior_dialog_synthesizes_only_dialog_bitmap()
    {
        var content = InteriorWizardContent("Welcome");
        var customization = new DialogCustomization()
            .DialogBitmap("C:/branding/dialog.bmp")
            .BannerBitmap("C:/branding/banner.bmp")
            .HeaderIcon("C:/branding/icon.ico")
            .ToModel();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270, customization);

        var bitmap = model.Controls.Single(c => c.Type == MsiControlType.Bitmap);
        Assert.Equal("DialogBmp", bitmap.Name);
        Assert.Equal("C:/branding/dialog.bmp", bitmap.Text);
        Assert.DoesNotContain(model.Controls, c => c.Type == MsiControlType.Icon);
    }
}
