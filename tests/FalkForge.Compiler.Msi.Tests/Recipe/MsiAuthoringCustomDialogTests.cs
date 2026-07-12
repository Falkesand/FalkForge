using System.Runtime.Versioning;
using FalkForge.Builders;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// End-to-end proof for the public custom-dialog authoring API: author a dialog with controls,
/// a control event, and a control condition; compile a real MSI; then open it and assert the
/// <c>Dialog</c>, <c>Control</c>, <c>ControlEvent</c>, and <c>ControlCondition</c> rows are
/// present and correct.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiAuthoringCustomDialogTests : IDisposable
{
    private readonly string _tempDir;

    public MsiAuthoringCustomDialogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MsiAuthoringCustomDlg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { }
        }
    }

    private PackageModel BuildPackageWithCustomDialog()
    {
        string sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        string sourceFile = Path.Combine(sourceDir, "app.txt");
        File.WriteAllText(sourceFile, "content");

        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "CustomDlgApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "CustomDlgApp"));

            p.AddCustomDialog("LicenseKeyDlg", dlg => dlg
                .Title("Enter your license key")
                .Sequence(1100)
                .FirstControl("KeyEdit")
                .Text("Prompt", 20, 20, 330, 20, "Please enter your license key:")
                .Edit("KeyEdit", 20, 50, 330, 18, property: "LICENSEKEY", b => b.Next("Next"))
                .PushButton("Next", 280, 240, 66, 17, "Next", b => b
                    .EndDialog("Return")
                    .DisableWhen("LICENSEKEY = \"\"")));
        });
    }

    [Fact]
    public void Authored_custom_dialog_compiles_and_the_dialog_row_is_present()
    {
        PackageModel package = BuildPackageWithCustomDialog();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(result.Value, readOnly: true).Value;
        Result<List<string?[]>> dialog = db.QueryRows(
            "SELECT `Dialog`, `Title`, `Control_First` FROM `Dialog` WHERE `Dialog` = 'LicenseKeyDlg'",
            fieldCount: 3);
        Assert.True(dialog.IsSuccess);
        string?[] row = Assert.Single(dialog.Value);
        Assert.Equal("LicenseKeyDlg", row[0]);
        Assert.Equal("Enter your license key", row[1]);
        Assert.Equal("KeyEdit", row[2]); // explicit FirstControl("KeyEdit")
    }

    [Fact]
    public void Authored_custom_dialog_control_rows_have_correct_type_position_and_property()
    {
        PackageModel package = BuildPackageWithCustomDialog();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(result.Value, readOnly: true).Value;
        Result<List<string?[]>> control = db.QueryRows(
            "SELECT `Control`, `Type`, `X`, `Y`, `Property`, `Control_Next` FROM `Control` " +
            "WHERE `Dialog_` = 'LicenseKeyDlg' AND `Control` = 'KeyEdit'",
            fieldCount: 6);
        Assert.True(control.IsSuccess);
        string?[] edit = Assert.Single(control.Value);
        Assert.Equal("KeyEdit", edit[0]);
        Assert.Equal("Edit", edit[1]);
        Assert.Equal("20", edit[2]);
        Assert.Equal("50", edit[3]);
        Assert.Equal("LICENSEKEY", edit[4]);
        Assert.Equal("Next", edit[5]);
    }

    [Fact]
    public void Authored_custom_dialog_control_event_and_condition_rows_are_present()
    {
        PackageModel package = BuildPackageWithCustomDialog();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(result.Value, readOnly: true).Value;

        Result<List<string?[]>> ce = db.QueryRows(
            "SELECT `Control_`, `Event`, `Argument` FROM `ControlEvent` WHERE `Dialog_` = 'LicenseKeyDlg'",
            fieldCount: 3);
        Assert.True(ce.IsSuccess);
        Assert.Contains(ce.Value, r => r[0] == "Next" && r[1] == "EndDialog" && r[2] == "Return");

        Result<List<string?[]>> cc = db.QueryRows(
            "SELECT `Control_`, `Action`, `Condition` FROM `ControlCondition` WHERE `Dialog_` = 'LicenseKeyDlg'",
            fieldCount: 3);
        Assert.True(cc.IsSuccess);
        Assert.Contains(cc.Value, r => r[0] == "Next" && r[1] == "Disable" && r[2] == "LICENSEKEY = \"\"");
    }

    [Fact]
    public void Authored_custom_dialog_is_scheduled_in_the_install_ui_sequence()
    {
        PackageModel package = BuildPackageWithCustomDialog();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(result.Value, readOnly: true).Value;
        Result<List<string?[]>> seq = db.QueryRows(
            "SELECT `Action`, `Sequence` FROM `InstallUISequence` WHERE `Action` = 'LicenseKeyDlg'",
            fieldCount: 2);
        Assert.True(seq.IsSuccess);
        string?[] row = Assert.Single(seq.Value);
        Assert.Equal("1100", row[1]);
    }
}
