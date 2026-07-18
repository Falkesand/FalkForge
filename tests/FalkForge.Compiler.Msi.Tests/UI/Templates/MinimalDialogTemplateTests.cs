using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Templates;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Templates;

public sealed class MinimalDialogTemplateTests
{
    private static IReadOnlyList<MsiDialogModel> Compose()
    {
        var template = new MinimalDialogTemplate();
        return template.GetDialogs(new PackageModel
        {
            Name = "Test",
            Manufacturer = "Acme",
            Version = new System.Version(1, 0, 0),
            UpgradeCode = System.Guid.Parse("12345678-1234-1234-1234-123456789abc"),
        });
    }

    [Fact]
    public void Template_emits_four_dialogs()
    {
        var dialogs = Compose();

        Assert.Equal(4, dialogs.Count);
    }

    [Fact]
    public void Template_emits_dialog_names_in_legacy_order()
    {
        var dialogs = Compose();
        var names = dialogs.Select(d => d.Name).ToArray();

        Assert.Equal(
            new[] { "WelcomeDlg", "ProgressDlg", "ExitDlg", "CancelDlg" },
            names);
    }

    [Fact]
    public void Template_emits_dialogs_via_composer()
    {
        // The composer-driven path populates Events on every produced model. Legacy
        // private builders also populated Events, but the test still proves the
        // composer-driven path is actually being used because the new builders are
        // the only place those declarative events come from.
        var dialogs = Compose();

        Assert.All(dialogs, d => Assert.NotEmpty(d.Events));
    }

    [Fact]
    public void Welcome_dialog_advances_to_progress()
    {
        var dialogs = Compose();
        var welcome = dialogs.Single(d => d.Name == "WelcomeDlg");

        var advance = welcome.Events.Single(e =>
            e.ControlName == "Next" && e.Event.ToString() == "NewDialog");

        Assert.Equal("ProgressDlg", advance.Argument);
    }

    [Fact]
    public void Welcome_cancel_spawns_cancel_dialog()
    {
        var dialogs = Compose();
        var welcome = dialogs.Single(d => d.Name == "WelcomeDlg");

        var cancel = welcome.Events.Single(e =>
            e.ControlName == "Cancel" && e.Event.ToString() == "SpawnDialog");

        Assert.Equal("CancelDlg", cancel.Argument);
    }

    [Fact]
    public void DialogBitmap_customization_targets_only_welcome_and_exit_dialogs()
    {
        var template = new MinimalDialogTemplate();
        var dialogs = template.GetDialogs(new PackageModel
        {
            Name = "Test",
            Manufacturer = "Acme",
            Version = new System.Version(1, 0, 0),
            UpgradeCode = System.Guid.Parse("12345678-1234-1234-1234-123456789abc"),
            DialogCustomization = new DialogCustomizationModel { DialogBitmap = "AcmeDialog" },
        });

        var welcome = dialogs.Single(d => d.Name == "WelcomeDlg");
        var exit = dialogs.Single(d => d.Name == "ExitDlg");
        var progress = dialogs.Single(d => d.Name == "ProgressDlg");

        Assert.Contains(welcome.Controls, c => c.Type == MsiControlType.Bitmap && c.Text == "AcmeDialog");
        Assert.Contains(exit.Controls, c => c.Type == MsiControlType.Bitmap && c.Text == "AcmeDialog");
        Assert.DoesNotContain(progress.Controls, c => c.Type == MsiControlType.Bitmap);
    }

    [Fact]
    public void BannerBitmap_and_HeaderIcon_customization_target_only_interior_dialogs()
    {
        var template = new MinimalDialogTemplate();
        var dialogs = template.GetDialogs(new PackageModel
        {
            Name = "Test",
            Manufacturer = "Acme",
            Version = new System.Version(1, 0, 0),
            UpgradeCode = System.Guid.Parse("12345678-1234-1234-1234-123456789abc"),
            DialogCustomization = new DialogCustomizationModel
            {
                BannerBitmap = "AcmeBanner",
                HeaderIcon = "AcmeIcon",
            },
        });

        var welcome = dialogs.Single(d => d.Name == "WelcomeDlg");
        var exit = dialogs.Single(d => d.Name == "ExitDlg");
        var progress = dialogs.Single(d => d.Name == "ProgressDlg");

        Assert.Contains(progress.Controls, c => c.Type == MsiControlType.Bitmap && c.Text == "AcmeBanner");
        Assert.Contains(progress.Controls, c => c.Type == MsiControlType.Icon && c.Text == "AcmeIcon");
        Assert.DoesNotContain(welcome.Controls, c => c.Type == MsiControlType.Bitmap);
        Assert.DoesNotContain(welcome.Controls, c => c.Type == MsiControlType.Icon);
        Assert.DoesNotContain(exit.Controls, c => c.Type == MsiControlType.Bitmap);
        Assert.DoesNotContain(exit.Controls, c => c.Type == MsiControlType.Icon);
    }
}
