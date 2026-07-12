using System;
using System.Collections.Immutable;
using System.Linq;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

/// <summary>
/// Tests that <see cref="DialogSetProducer"/> emits author-defined
/// <see cref="PackageModel.CustomDialogs"/> into the MSI UI tables — including when no stock
/// <see cref="MsiDialogSet"/> is active — with the correct Control, ControlEvent, and
/// ControlCondition rows.
/// </summary>
public sealed class DialogSetProducerCustomDialogTests
{
    private static CustomDialogModel LicenseKeyDialog() => new()
    {
        Id = "LicenseKeyDlg",
        Title = "Enter license key",
        FirstControl = "KeyEdit",
        Controls =
        [
            new CustomDialogControlModel
            {
                Name = "Prompt", Type = CustomControlType.Text,
                X = 20, Y = 20, Width = 330, Height = 20, Text = "Enter your key:",
            },
            new CustomDialogControlModel
            {
                Name = "KeyEdit", Type = CustomControlType.Edit,
                X = 20, Y = 50, Width = 330, Height = 18, Property = "LICENSEKEY",
                NextControl = "Next",
            },
            new CustomDialogControlModel
            {
                Name = "Next", Type = CustomControlType.PushButton,
                X = 280, Y = 240, Width = 66, Height = 17, Text = "Next",
                Events =
                [
                    new CustomDialogControlEventModel { Event = "NewDialog", Argument = "ExitDlg" },
                ],
                Conditions =
                [
                    new CustomDialogControlConditionModel
                    {
                        Action = CustomConditionAction.Disable, Condition = "LICENSEKEY = \"\"",
                    },
                ],
            },
        ],
    };

    private static ImmutableArray<RecipeTable> Produce(PackageModel package)
    {
        var ctx = new RecipeBuildContext(
            new ResolvedPackage { Package = package, Components = [], Files = [] },
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        Result<ImmutableArray<RecipeTable>> result = new DialogSetProducer().Produce(ctx);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static RecipeTable Table(ImmutableArray<RecipeTable> tables, string name)
        => tables.First(t => t.Name.Value == name);

    private static string Str(CellValue cell) => ((CellValue.StringValue)cell).Value;

    [Fact]
    public void Custom_dialog_with_DialogSet_None_still_emits_ui_tables()
    {
        PackageModel package = new()
        {
            Name = "App", Manufacturer = "M", Version = new Version(1, 0, 0),
            DialogSet = MsiDialogSet.None,
            CustomDialogs = [LicenseKeyDialog()],
        };

        ImmutableArray<RecipeTable> tables = Produce(package);

        Assert.Contains(tables, t => t.Name.Value == "Dialog");
        RecipeTable dialog = Table(tables, "Dialog");
        Assert.Contains(dialog.Rows, r => Str(r.Cells[0]) == "LicenseKeyDlg");
    }

    [Fact]
    public void Custom_dialog_emits_control_rows_with_correct_type_position_and_property()
    {
        ImmutableArray<RecipeTable> tables = Produce(new PackageModel
        {
            Name = "App", Manufacturer = "M", Version = new Version(1, 0, 0),
            CustomDialogs = [LicenseKeyDialog()],
        });

        RecipeTable control = Table(tables, "Control");
        // Control columns: Dialog_, Control, Type, X, Y, Width, Height, Attributes, Property, Text, Control_Next, Help
        RecipeRow edit = control.Rows.Single(r =>
            Str(r.Cells[0]) == "LicenseKeyDlg" && Str(r.Cells[1]) == "KeyEdit");

        Assert.Equal("Edit", Str(edit.Cells[2]));
        Assert.Equal(20, ((CellValue.IntValue)edit.Cells[3]).Value);   // X
        Assert.Equal(50, ((CellValue.IntValue)edit.Cells[4]).Value);   // Y
        Assert.Equal("LICENSEKEY", Str(edit.Cells[8]));                 // Property
        Assert.Equal("Next", Str(edit.Cells[10]));                     // Control_Next
    }

    [Fact]
    public void Custom_dialog_emits_control_event_row()
    {
        ImmutableArray<RecipeTable> tables = Produce(new PackageModel
        {
            Name = "App", Manufacturer = "M", Version = new Version(1, 0, 0),
            CustomDialogs = [LicenseKeyDialog()],
        });

        RecipeTable ce = Table(tables, "ControlEvent");
        // ControlEvent columns: Dialog_, Control_, Event, Argument, Condition, Ordering
        Assert.Contains(ce.Rows, r =>
            Str(r.Cells[0]) == "LicenseKeyDlg" &&
            Str(r.Cells[1]) == "Next" &&
            Str(r.Cells[2]) == "NewDialog" &&
            Str(r.Cells[3]) == "ExitDlg");
    }

    [Fact]
    public void Custom_dialog_emits_control_condition_row()
    {
        ImmutableArray<RecipeTable> tables = Produce(new PackageModel
        {
            Name = "App", Manufacturer = "M", Version = new Version(1, 0, 0),
            CustomDialogs = [LicenseKeyDialog()],
        });

        RecipeTable cc = Table(tables, "ControlCondition");
        // ControlCondition columns: Dialog_, Control_, Action, Condition
        Assert.Contains(cc.Rows, r =>
            Str(r.Cells[0]) == "LicenseKeyDlg" &&
            Str(r.Cells[1]) == "Next" &&
            Str(r.Cells[2]) == "Disable" &&
            Str(r.Cells[3]) == "LICENSEKEY = \"\"");
    }

    [Fact]
    public void Custom_dialog_appended_alongside_stock_set()
    {
        ImmutableArray<RecipeTable> tables = Produce(new PackageModel
        {
            Name = "App", Manufacturer = "M", Version = new Version(1, 0, 0),
            DialogSet = MsiDialogSet.Minimal,
            CustomDialogs = [LicenseKeyDialog()],
        });

        RecipeTable dialog = Table(tables, "Dialog");
        Assert.Contains(dialog.Rows, r => Str(r.Cells[0]) == "WelcomeDlg");     // stock
        Assert.Contains(dialog.Rows, r => Str(r.Cells[0]) == "LicenseKeyDlg");  // custom
    }
}
