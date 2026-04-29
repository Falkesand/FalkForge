using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Templates;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Templates;

public sealed class AdvancedDialogTemplateTests
{
    private static IReadOnlyList<MsiDialogModel> Compose()
    {
        var template = new AdvancedDialogTemplate();
        return template.GetDialogs(new PackageModel
        {
            Name = "Test",
            Manufacturer = "Acme",
            Version = new System.Version(1, 0, 0),
            UpgradeCode = System.Guid.Parse("12345678-1234-1234-1234-123456789abc"),
        });
    }

    [Fact]
    public void Template_emits_ten_dialogs_in_legacy_order()
    {
        var names = Compose().Select(d => d.Name).ToArray();

        Assert.Equal(
            new[]
            {
                "WelcomeDlg",
                "InstallScopeDlg",
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
    public void Welcome_advances_to_install_scope()
    {
        var welcome = Compose().Single(d => d.Name == "WelcomeDlg");
        var next = welcome.Events.Single(e =>
            e.ControlName == "Next" && e.Event.ToString() == "NewDialog");

        Assert.Equal("InstallScopeDlg", next.Argument);
    }

    [Fact]
    public void InstallScope_back_returns_to_welcome()
    {
        var scope = Compose().Single(d => d.Name == "InstallScopeDlg");
        var back = scope.Events.Single(e =>
            e.ControlName == "Back" && e.Event.ToString() == "NewDialog");

        Assert.Equal("WelcomeDlg", back.Argument);
    }

    [Fact]
    public void InstallScope_per_machine_advances_to_license()
    {
        var scope = Compose().Single(d => d.Name == "InstallScopeDlg");
        var advance = scope.Events.Single(e =>
            e.ControlName == "PerMachine" && e.Event.ToString() == "NewDialog");

        Assert.Equal("LicenseAgreementDlg", advance.Argument);
    }

    [Fact]
    public void InstallScope_per_user_advances_to_license()
    {
        var scope = Compose().Single(d => d.Name == "InstallScopeDlg");
        var advance = scope.Events.Single(e =>
            e.ControlName == "PerUser" && e.Event.ToString() == "NewDialog");

        Assert.Equal("LicenseAgreementDlg", advance.Argument);
    }

    [Fact]
    public void License_back_returns_to_install_scope()
    {
        var license = Compose().Single(d => d.Name == "LicenseAgreementDlg");
        var back = license.Events.Single(e =>
            e.ControlName == "Back" && e.Event.ToString() == "NewDialog");

        Assert.Equal("InstallScopeDlg", back.Argument);
    }
}
