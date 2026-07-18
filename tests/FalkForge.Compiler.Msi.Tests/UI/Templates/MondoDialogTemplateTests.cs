using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Templates;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Templates;

public sealed class MondoDialogTemplateTests
{
    private static IReadOnlyList<MsiDialogModel> Compose()
    {
        var template = new MondoDialogTemplate();
        return template.GetDialogs(new PackageModel
        {
            Name = "Test",
            Manufacturer = "Acme",
            Version = new System.Version(1, 0, 0),
            UpgradeCode = System.Guid.Parse("12345678-1234-1234-1234-123456789abc"),
        });
    }

    [Fact]
    public void Template_emits_nine_dialogs_in_legacy_order()
    {
        var names = Compose().Select(d => d.Name).ToArray();

        Assert.Equal(
            new[]
            {
                "WelcomeDlg",
                "LicenseAgreementDlg",
                "SetupTypeDlg",
                "CustomizeDlg",
                "InstallDirDlg",
                "ProgressDlg",
                "ExitDlg",
                "CancelDlg",
                "BrowseDlg",
            },
            names);
    }

    [Fact]
    public void Template_emits_dialogs_via_composer()
    {
        var dialogs = Compose();
        var welcome = dialogs.Single(d => d.Name == "WelcomeDlg");
        var title = welcome.Controls.Single(c => c.Name == "Title");

        Assert.Equal(
            MsiControlAttributes.Visible | MsiControlAttributes.Enabled,
            title.Attributes);
    }

    [Fact]
    public void License_advances_to_setup_type()
    {
        var license = Compose().Single(d => d.Name == "LicenseAgreementDlg");

        var next = license.Events.Single(e =>
            e.ControlName == "Next" && e.Event.ToString() == "NewDialog");

        Assert.Equal("SetupTypeDlg", next.Argument);
    }

    [Fact]
    public void Customize_back_returns_to_setup_type()
    {
        var customize = Compose().Single(d => d.Name == "CustomizeDlg");

        var back = customize.Events.Single(e =>
            e.ControlName == "Back" && e.Event.ToString() == "NewDialog");

        Assert.Equal("SetupTypeDlg", back.Argument);
    }

    [Fact]
    public void InstallDir_back_returns_to_setup_type()
    {
        var installDir = Compose().Single(d => d.Name == "InstallDirDlg");

        var back = installDir.Events.Single(e =>
            e.ControlName == "Back" && e.Event.ToString() == "NewDialog");

        Assert.Equal("SetupTypeDlg", back.Argument);
    }

    [Fact]
    public void DialogBitmap_customization_targets_only_welcome_and_exit_dialogs()
    {
        var template = new MondoDialogTemplate();
        var dialogs = template.GetDialogs(new PackageModel
        {
            Name = "Test",
            Manufacturer = "Acme",
            Version = new System.Version(1, 0, 0),
            UpgradeCode = System.Guid.Parse("12345678-1234-1234-1234-123456789abc"),
            DialogCustomization = new DialogCustomizationModel { DialogBitmap = "background.bmp" },
        });

        var welcome = dialogs.Single(d => d.Name == "WelcomeDlg");
        var exit = dialogs.Single(d => d.Name == "ExitDlg");
        var setupType = dialogs.Single(d => d.Name == "SetupTypeDlg");

        Assert.Contains(welcome.Controls, c => c.Type == MsiControlType.Bitmap && c.Text == "background.bmp");
        Assert.Contains(exit.Controls, c => c.Type == MsiControlType.Bitmap && c.Text == "background.bmp");
        Assert.DoesNotContain(setupType.Controls, c => c.Type == MsiControlType.Bitmap);
    }
}
