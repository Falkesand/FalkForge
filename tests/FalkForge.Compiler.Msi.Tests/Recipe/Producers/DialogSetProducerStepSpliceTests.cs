using System;
using System.Collections.Immutable;
using System.Linq;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

/// <summary>
/// Pins that an extension step named by <c>InsertStep</c> is spliced into the wizard flow, rather
/// than emitted as a dialog nothing navigates to.
/// </summary>
/// <remarks>
/// The button labels are the point of this file, not an afterthought. A splice moves which dialog
/// starts the install, and <see cref="DialogFooter.NextButton"/> derives its label from the event
/// the button fires. Splicing after composition was measured leaving the anchor reading "Install"
/// while it only advanced a page, and the step reading "Next" while it started the install. Both
/// wrong, on both sides of the splice. Splicing before composition makes the labels fall out
/// correctly with no label logic outside DialogFooter.
/// </remarks>
public sealed class DialogSetProducerStepSpliceTests
{
    /// <summary>
    /// A step builder shaped like the one the architecture doc teaches, except that it consults
    /// the flow it is handed instead of hardcoding one. A builder that ignores the flow cannot
    /// know its own forward target and emits a NewDialog with an empty argument.
    /// </summary>
    private sealed class ProbeStepBuilder : IMsiDialogStepBuilder
    {
        public string Name => "ProbeStep";

        public MsiDialogModel Build(DialogBuildContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            DialogControlEvent next = DialogFooter.NextEvent(context.Flow);

            return DialogComposer.Compose(
                new DialogContent
                {
                    Name = Name,
                    Kind = "Extension",
                    FirstControl = "Next",
                    DefaultControl = "Next",
                    CancelControl = "Cancel",
                    TitleLocKey = "[ProductName] Setup",
                    Events = ImmutableArray.Create(
                        next,
                        DialogFooter.BackEvent(context.Flow),
                        DialogFooter.CancelEvent(context.Flow)),
                    Placements = ImmutableArray.Create(
                        DialogFooter.BottomLine(),
                        new RegionPlacement
                        {
                            RegionName = "ButtonRow",
                            Controls = ImmutableArray.Create(
                                DialogFooter.CancelButton(),
                                DialogFooter.NextButton(next),
                                DialogFooter.BackButton()),
                        }),
                },
                Layouts.Standard370x270,
                context.Customization);
        }
    }

    private static ImmutableArray<RecipeTable> Produce(MsiDialogSet set, DialogStepAnchor anchor)
    {
        PackageModel package = new()
        {
            Name = "App",
            Manufacturer = "M",
            Version = new Version(1, 0, 0),
            DialogSet = set,
            DialogCustomization = new DialogCustomization()
                .InsertStep("ProbeStep", anchor)
                .ToModel(),
        };

        var ctx = new RecipeBuildContext(
            new ResolvedPackage { Package = package, Components = [], Files = [] },
            new DictionaryStreamRegistry());

        Result<ImmutableArray<RecipeTable>> result =
            new DialogSetProducer([new ProbeStepBuilder()]).Produce(ctx);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static string Str(CellValue cell) => ((CellValue.StringValue)cell).Value;

    private static string ButtonText(ImmutableArray<RecipeTable> tables, string dialog, string control)
    {
        RecipeTable table = tables.First(t => t.Name.Value == "Control");
        RecipeRow row = table.Rows.Single(r => Str(r.Cells[0]) == dialog && Str(r.Cells[1]) == control);
        return Str(row.Cells[9]);
    }

    private static (string Event, string Argument) NextEventOf(ImmutableArray<RecipeTable> tables, string dialog)
    {
        RecipeTable table = tables.First(t => t.Name.Value == "ControlEvent");
        RecipeRow row = table.Rows.Single(r => Str(r.Cells[0]) == dialog && Str(r.Cells[1]) == "Next");
        return (Str(row.Cells[2]), Str(row.Cells[3]));
    }

    [Fact]
    public void A_step_inserted_after_Welcome_in_the_Minimal_set_is_navigated_to()
    {
        ImmutableArray<RecipeTable> tables = Produce(MsiDialogSet.Minimal, DialogStepAnchor.Welcome);

        Assert.Equal(("NewDialog", "ProbeStep"), NextEventOf(tables, "WelcomeDlg"));
        Assert.Equal(("EndDialog", "Return"), NextEventOf(tables, "ProbeStep"));
    }

    [Fact]
    public void The_splice_moves_the_Install_label_onto_the_dialog_that_now_starts_the_install()
    {
        // The regression that sinks a post-composition splice. In the Minimal set WelcomeDlg is
        // the last page before the install, so its button reads Install. Once a step is spliced in
        // after it, WelcomeDlg merely advances a page and the step starts the install, so the
        // labels must swap. Retargeting control events after composition leaves both stale.
        ImmutableArray<RecipeTable> tables = Produce(MsiDialogSet.Minimal, DialogStepAnchor.Welcome);

        Assert.Equal("&Next >", ButtonText(tables, "WelcomeDlg", "Next"));
        Assert.Equal("&Install", ButtonText(tables, "ProbeStep", "Next"));
    }

    [Fact]
    public void The_same_swap_holds_for_the_InstallDir_set()
    {
        // InstallDirDlg publishes the install handoff directly rather than through a flow target,
        // so it is the builder most likely to ignore a spliced flow and keep the stale label.
        ImmutableArray<RecipeTable> tables = Produce(MsiDialogSet.InstallDir, DialogStepAnchor.InstallDir);

        Assert.Equal(("NewDialog", "ProbeStep"), NextEventOf(tables, "InstallDirDlg"));
        Assert.Equal(("EndDialog", "Return"), NextEventOf(tables, "ProbeStep"));
        Assert.Equal("&Next >", ButtonText(tables, "InstallDirDlg", "Next"));
        Assert.Equal("&Install", ButtonText(tables, "ProbeStep", "Next"));
    }

    [Fact]
    public void A_step_spliced_mid_chain_keeps_the_Install_label_where_it_was()
    {
        // The complement of the case above. Inserting after Welcome in the FeatureTree set puts the
        // step between two pages, so no dialog changes install-start status and no label moves.
        // A splice that swapped labels unconditionally would break this.
        ImmutableArray<RecipeTable> tables = Produce(MsiDialogSet.FeatureTree, DialogStepAnchor.Welcome);

        Assert.Equal(("NewDialog", "ProbeStep"), NextEventOf(tables, "WelcomeDlg"));
        Assert.Equal("&Next >", ButtonText(tables, "WelcomeDlg", "Next"));
        Assert.Equal("&Next >", ButtonText(tables, "ProbeStep", "Next"));
        Assert.Equal("&Install", ButtonText(tables, "CustomizeDlg", "Next"));
    }

    [Fact]
    public void BeforeInstall_puts_the_step_last_whatever_the_set()
    {
        // The set-independent anchor, and the one that replaces StockDialog.Extension, which was
        // documented as appending at the end of the sequence and implemented as nothing.
        foreach (MsiDialogSet set in new[]
        {
            MsiDialogSet.Minimal, MsiDialogSet.FeatureTree, MsiDialogSet.Mondo,
            MsiDialogSet.Advanced, MsiDialogSet.InstallDir,
        })
        {
            ImmutableArray<RecipeTable> tables = Produce(set, DialogStepAnchor.BeforeInstall);

            Assert.Equal(("EndDialog", "Return"), NextEventOf(tables, "ProbeStep"));
            Assert.Equal("&Install", ButtonText(tables, "ProbeStep", "Next"));
        }
    }

    [Fact]
    public void The_step_can_go_back_to_the_dialog_it_follows()
    {
        ImmutableArray<RecipeTable> tables = Produce(MsiDialogSet.FeatureTree, DialogStepAnchor.Welcome);

        RecipeTable events = tables.First(t => t.Name.Value == "ControlEvent");
        RecipeRow back = events.Rows.Single(r => Str(r.Cells[0]) == "ProbeStep" && Str(r.Cells[1]) == "Back");

        Assert.Equal("NewDialog", Str(back.Cells[2]));
        Assert.Equal("WelcomeDlg", Str(back.Cells[3]));
    }

    [Fact]
    public void The_dialog_after_the_step_goes_back_to_the_step_not_past_it()
    {
        // Splicing forward is not symmetric with splicing back. Retargeting only the step's own
        // Back leaves the following dialog's Back pointing past the step, so Back skips a page.
        ImmutableArray<RecipeTable> tables = Produce(MsiDialogSet.FeatureTree, DialogStepAnchor.Welcome);

        RecipeTable events = tables.First(t => t.Name.Value == "ControlEvent");
        RecipeRow back = events.Rows.Single(r =>
            Str(r.Cells[0]) == "LicenseAgreementDlg" && Str(r.Cells[1]) == "Back");

        Assert.Equal("ProbeStep", Str(back.Cells[3]));
    }
}
