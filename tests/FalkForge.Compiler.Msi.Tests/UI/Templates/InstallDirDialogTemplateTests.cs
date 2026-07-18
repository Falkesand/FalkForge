using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Templates;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Templates;

public sealed class InstallDirDialogTemplateTests
{
    private static IReadOnlyList<MsiDialogModel> Compose()
    {
        var template = new InstallDirDialogTemplate();
        return template.GetDialogs(new PackageModel
        {
            Name = "Test",
            Manufacturer = "Acme",
            Version = new System.Version(1, 0, 0),
            UpgradeCode = System.Guid.Parse("12345678-1234-1234-1234-123456789abc"),
        });
    }

    [Fact]
    public void Template_emits_seven_dialogs_in_legacy_order()
    {
        var names = Compose().Select(d => d.Name).ToArray();

        Assert.Equal(
            new[]
            {
                "WelcomeDlg",
                "LicenseAgreementDlg",
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
        // Composer-driven Title controls inherit MsiControlModel's default Attributes
        // (Visible|Enabled) because PlacedControl carries no Attributes column. Legacy
        // hand-coded builders explicitly set Attributes to Visible|Enabled|Transparent|NoPrefix.
        // Asserting the composer default proves the composer-driven path is in effect.
        var dialogs = Compose();

        var welcome = dialogs.Single(d => d.Name == "WelcomeDlg");
        var title = welcome.Controls.Single(c => c.Name == "Title");

        Assert.Equal(
            MsiControlAttributes.Visible | MsiControlAttributes.Enabled,
            title.Attributes);
    }

    [Fact]
    public void Welcome_advances_to_license()
    {
        var welcome = Compose().Single(d => d.Name == "WelcomeDlg");

        var advance = welcome.Events.Single(e =>
            e.ControlName == "Next" && e.Event.ToString() == "NewDialog");

        Assert.Equal("LicenseAgreementDlg", advance.Argument);
    }

    [Fact]
    public void License_back_returns_to_welcome_and_next_advances_to_installdir()
    {
        var license = Compose().Single(d => d.Name == "LicenseAgreementDlg");

        var back = license.Events.Single(e =>
            e.ControlName == "Back" && e.Event.ToString() == "NewDialog");
        var next = license.Events.Single(e =>
            e.ControlName == "Next" && e.Event.ToString() == "NewDialog");

        Assert.Equal("WelcomeDlg", back.Argument);
        Assert.Equal("InstallDirDlg", next.Argument);
    }

    [Fact]
    public void InstallDir_back_returns_to_license()
    {
        var installDir = Compose().Single(d => d.Name == "InstallDirDlg");

        var back = installDir.Events.Single(e =>
            e.ControlName == "Back" && e.Event.ToString() == "NewDialog");

        Assert.Equal("LicenseAgreementDlg", back.Argument);
    }

    [Fact]
    public void DialogBitmap_customization_targets_only_welcome_and_exit_dialogs()
    {
        var template = new InstallDirDialogTemplate();
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
        var installDir = dialogs.Single(d => d.Name == "InstallDirDlg");

        Assert.Contains(welcome.Controls, c => c.Type == MsiControlType.Bitmap && c.Text == "background.bmp");
        Assert.Contains(exit.Controls, c => c.Type == MsiControlType.Bitmap && c.Text == "background.bmp");
        Assert.DoesNotContain(installDir.Controls, c => c.Type == MsiControlType.Bitmap);
    }
}
