using System.Linq;
using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests;

/// <summary>
/// Tests for the public custom MSI dialog authoring API exposed through
/// <see cref="PackageBuilder.AddCustomDialog(string, System.Action{CustomDialogBuilder})"/>.
/// These verify that a fully-authored dialog (controls, events, conditions, tab order)
/// materialises onto <see cref="PackageModel.CustomDialogs"/> with the intended shape —
/// the author-facing contract that the compiler later translates into MSI UI tables.
/// </summary>
public sealed class CustomDialogAuthoringTests
{
    private static PackageBuilder NewBuilder()
    {
        var b = new PackageBuilder
        {
            Name = "CustomDlgApp",
            Manufacturer = "FalkForge",
            Version = new System.Version(1, 0, 0),
        };
        return b;
    }

    [Fact]
    public void AddCustomDialog_records_a_dialog_with_its_controls()
    {
        PackageModel package = NewBuilder()
            .AddCustomDialog("LicenseKeyDlg", dlg => dlg
                .Title("Enter your license key")
                .Size(370, 270)
                .Text("Prompt", 20, 20, 330, 20, "Please enter your license key:")
                .Edit("KeyEdit", 20, 50, 330, 18, property: "LICENSEKEY")
                .PushButton("Next", 280, 240, 66, 17, "Next", b => b.NavigateTo("ExitDlg")))
            .Build();

        Assert.Single(package.CustomDialogs);
        CustomDialogModel dlg = package.CustomDialogs[0];
        Assert.Equal("LicenseKeyDlg", dlg.Id);
        Assert.Equal("Enter your license key", dlg.Title);
        Assert.Equal(3, dlg.Controls.Count);

        CustomDialogControlModel edit = dlg.Controls.Single(c => c.Name == "KeyEdit");
        Assert.Equal(CustomControlType.Edit, edit.Type);
        Assert.Equal("LICENSEKEY", edit.Property);
        Assert.Equal(20, edit.X);
        Assert.Equal(50, edit.Y);
    }

    [Fact]
    public void PushButton_NavigateTo_records_a_NewDialog_control_event()
    {
        PackageModel package = NewBuilder()
            .AddCustomDialog("WelcomeDlg", dlg => dlg
                .PushButton("Next", 280, 240, 66, 17, "Next", b => b.NavigateTo("ExitDlg")))
            .Build();

        CustomDialogControlModel next = package.CustomDialogs[0].Controls.Single();
        CustomDialogControlEventModel evt = Assert.Single(next.Events);
        Assert.Equal("NewDialog", evt.Event);
        Assert.Equal("ExitDlg", evt.Argument);
    }

    [Fact]
    public void CheckBox_When_records_a_control_condition()
    {
        PackageModel package = NewBuilder()
            .AddCustomDialog("OptionsDlg", dlg => dlg
                .CheckBox("Agree", 20, 20, 330, 18, property: "ACCEPTEULA", text: "I accept")
                .PushButton("Install", 280, 240, 66, 17, "Install",
                    b => b.DisableWhen("ACCEPTEULA <> \"1\"")))
            .Build();

        CustomDialogControlModel install = package.CustomDialogs[0].Controls.Single(c => c.Name == "Install");
        CustomDialogControlConditionModel cond = Assert.Single(install.Conditions);
        Assert.Equal(CustomConditionAction.Disable, cond.Action);
        Assert.Equal("ACCEPTEULA <> \"1\"", cond.Condition);
    }

    [Fact]
    public void Sequence_marks_the_dialog_as_an_install_ui_entry()
    {
        PackageModel package = NewBuilder()
            .AddCustomDialog("WelcomeDlg", dlg => dlg
                .Sequence(1100)
                .PushButton("Next", 280, 240, 66, 17, "Next", b => b.EndDialog("Return")))
            .Build();

        Assert.Equal(1100, package.CustomDialogs[0].SequenceNumber);
    }
}
