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
        RecipeRow row = table.Rows.Single(r => Str(r.Cells[0]) == dialog && Str(r.Cells[1]) == "Next"
            && Str(r.Cells[2]) is "NewDialog" or "EndDialog");
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

            // "Last whatever the set" has to mean every path, not just the main one. On Mondo and
            // Advanced the setup-type dialog's Typical and Complete buttons start the install
            // directly, so a user choosing Typical would skip the step unless those route through
            // it too. Asserting only the step's own event would pass while that hole was open.
            // Only reachable wizard pages count. ExitDlg's Finish and the support modals also
            // publish EndDialog/Return, legitimately, because they end a dialog AFTER the install
            // or return from a spawned child. Folder selection is reachable and must not bypass the step.
            string[] notReachableWizardPages =
                ["ExitDlg", "CancelDlg", "BrowseDlg", "MsiRMFilesInUse"];
            RecipeTable events = tables.First(t => t.Name.Value == "ControlEvent");
            foreach (RecipeRow row in events.Rows)
            {
                string dialog = Str(row.Cells[0]);
                if (Str(row.Cells[2]) == "EndDialog"
                    && Str(row.Cells[3]) == "Return"
                    && !notReachableWizardPages.Contains(dialog))
                {
                    Assert.Equal("ProbeStep", dialog);
                }
            }
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

    /// <summary>
    /// A step builder that hardcodes its own flow instead of consulting the one it is handed. This
    /// is the shape the architecture doc taught before this change, and it cannot know its forward
    /// target, so it publishes a NewDialog with an empty argument.
    /// </summary>
    private sealed class FlowIgnoringStepBuilder : IMsiDialogStepBuilder
    {
        public string Name => "ProbeStep";

        public MsiDialogModel Build(DialogBuildContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var model = new MsiDialogModel { Name = Name, FirstControl = "Body" };
            model.Controls.Add(new MsiControlModel
            {
                Name = "Body", Type = MsiControlType.Text,
                X = 20, Y = 20, Width = 330, Height = 40, Text = "Nothing here advances.",
            });
            return model;
        }
    }

    private static Result<ImmutableArray<RecipeTable>> ProduceRaw(
        MsiDialogSet set, IMsiDialogStepBuilder? builder, params (string Step, DialogStepAnchor Anchor)[] steps)
    {
        var customization = new DialogCustomization();
        foreach ((string step, DialogStepAnchor anchor) in steps)
        {
            customization = customization.InsertStep(step, anchor);
        }

        PackageModel package = new()
        {
            Name = "App",
            Manufacturer = "M",
            Version = new Version(1, 0, 0),
            DialogSet = set,
            DialogCustomization = customization.ToModel(),
        };

        var ctx = new RecipeBuildContext(
            new ResolvedPackage { Package = package, Components = [], Files = [] },
            new DictionaryStreamRegistry());

        return new DialogSetProducer(builder is null ? [] : [builder]).Produce(ctx);
    }

    [Fact]
    public void A_step_that_ignores_the_flow_it_was_handed_fails_the_build()
    {
        // The splice is correct by construction for the stock dialogs and correct by COOPERATION
        // for the step, because a builder can simply not consult the flow. Nothing in the type
        // system enforces it, so the compiler checks rather than trusting. Without this the splice
        // ships a compiling installer with a page the user cannot advance past.
        Result<ImmutableArray<RecipeTable>> result = ProduceRaw(
            MsiDialogSet.Minimal, new FlowIgnoringStepBuilder(), ("ProbeStep", DialogStepAnchor.Welcome));

        Assert.True(result.IsFailure);
        Assert.Contains("DLG025", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("ProbeStep", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_anchor_that_the_active_set_does_not_contain_fails_the_build()
    {
        // The Minimal set has no licence dialog, so this anchor names a page that is not there.
        // It was a silent no-op before: the step was emitted and nothing navigated to it.
        Result<ImmutableArray<RecipeTable>> result = ProduceRaw(
            MsiDialogSet.Minimal, new ProbeStepBuilder(), ("ProbeStep", DialogStepAnchor.License));

        Assert.True(result.IsFailure);
        Assert.Contains("DLG026", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("License", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_step_inserted_at_two_anchors_fails_the_build()
    {
        // One dialog cannot occupy two positions in one chain. This used to emit a single dialog
        // row and silently drop the second insertion point.
        Result<ImmutableArray<RecipeTable>> result = ProduceRaw(
            MsiDialogSet.FeatureTree,
            new ProbeStepBuilder(),
            ("ProbeStep", DialogStepAnchor.Welcome),
            ("ProbeStep", DialogStepAnchor.Features));

        Assert.True(result.IsFailure);
        Assert.Contains("DLG027", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("ProbeStep", result.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Publishes the right verb and argument, but on a control the dialog never defines, or behind
    /// a condition that is never true, or on a control the user cannot see or click. Each looks
    /// correct in the event table and none of them gives the user a way forward.
    /// </summary>
    private sealed class UnreachableEdgeStepBuilder(string flavour) : IMsiDialogStepBuilder
    {
        public string Name => "ProbeStep";

        public MsiDialogModel Build(DialogBuildContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            DialogControlEvent next = DialogFooter.NextEvent(context.Flow);
            var model = new MsiDialogModel { Name = Name, FirstControl = "Go" };

            MsiControlAttributes attributes = flavour switch
            {
                "hidden" => MsiControlAttributes.Enabled,
                "disabled" => MsiControlAttributes.Visible,
                _ => MsiControlAttributes.Visible | MsiControlAttributes.Enabled,
            };

            model.Controls.Add(new MsiControlModel
            {
                Name = "Go", Type = MsiControlType.PushButton,
                X = 280, Y = 240, Width = 66, Height = 17, Text = "Go", Attributes = attributes,
            });

            model.Events.Add(new MsiControlEventModel
            {
                DialogName = Name,
                ControlName = flavour == "ghost" ? "NoSuchControl" : "Go",
                Event = MsiControlEvent.Parse(next.Event),
                Argument = next.Argument,
                Condition = flavour == "condition" ? "0" : null,
            });

            return model;
        }
    }

    [Theory]
    [InlineData("ghost")]
    [InlineData("condition")]
    [InlineData("hidden")]
    [InlineData("disabled")]
    public void A_forward_edge_the_user_cannot_reach_does_not_satisfy_the_check(string flavour)
    {
        // Matching only on the verb and argument makes the check look like protection it is not.
        // Each of these publishes exactly the right event and still leaves the user stuck: on a
        // control that does not exist, behind a condition that never fires, or on a button they
        // cannot see or click.
        Result<ImmutableArray<RecipeTable>> result = ProduceRaw(
            MsiDialogSet.Minimal, new UnreachableEdgeStepBuilder(flavour), ("ProbeStep", DialogStepAnchor.Welcome));

        Assert.True(result.IsFailure, $"'{flavour}' was accepted");
        Assert.Contains("DLG025", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_anchored_before_the_install_stays_last_when_another_step_is_added_after_it()
    {
        // BeforeInstall resolved against the chain as it stood would let a later named insertion
        // land behind it, so the step that asked to be last became second to last and the install
        // handoff went to the step that did not ask for it.
        var customization = new DialogCustomization()
            .InsertStep("LastStep", DialogStepAnchor.BeforeInstall)
            .InsertStep("MiddleStep", DialogStepAnchor.Welcome)
            .ToModel();

        PackageModel package = new()
        {
            Name = "App", Manufacturer = "M", Version = new Version(1, 0, 0),
            DialogSet = MsiDialogSet.FeatureTree,
            DialogCustomization = customization,
        };

        var ctx = new RecipeBuildContext(
            new ResolvedPackage { Package = package, Components = [], Files = [] },
            new DictionaryStreamRegistry());

        Result<ImmutableArray<RecipeTable>> result = new DialogSetProducer(
            [new NamedProbeStepBuilder("LastStep"), new NamedProbeStepBuilder("MiddleStep")]).Produce(ctx);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        RecipeTable events = result.Value.First(t => t.Name.Value == "ControlEvent");
        RecipeRow last = events.Rows.Single(r => Str(r.Cells[0]) == "LastStep" && Str(r.Cells[1]) == "Go");
        RecipeRow middle = events.Rows.Single(r => Str(r.Cells[0]) == "MiddleStep" && Str(r.Cells[1]) == "Go");

        Assert.Equal(("EndDialog", "Return"), (Str(last.Cells[2]), Str(last.Cells[3])));
        Assert.Equal(("NewDialog", "LicenseAgreementDlg"), (Str(middle.Cells[2]), Str(middle.Cells[3])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_missing_or_misnamed_step_reports_the_registered_step(bool misnamed)
    {
        var result = ProduceRaw(MsiDialogSet.Minimal,
            misnamed ? new NamedProbeStepBuilder("ProbeStep", emittedName: "WrongName") : null,
            ("ProbeStep", DialogStepAnchor.BeforeInstall));

        Assert.True(result.IsFailure);
        Assert.Contains("DLG025", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("ProbeStep", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", 3, false)]
    [InlineData(null, 3, false)]
    [InlineData(null, 1, true)]
    [InlineData("0", 3, true)]
    public void A_fallback_forward_event_must_not_be_suppressed(string? siblingCondition, int siblingOrder, bool accepted)
    {
        var result = ProduceRaw(MsiDialogSet.Minimal,
            new NamedProbeStepBuilder("ProbeStep", siblingCondition: siblingCondition, siblingOrder: siblingOrder),
            ("ProbeStep", DialogStepAnchor.BeforeInstall));

        Assert.Equal(accepted, result.IsSuccess);
        if (!accepted)
            Assert.Contains("DLG025", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void Direct_install_routes_visit_every_trailing_step(MsiDialogSet set)
    {
        var package = new PackageModel
        {
            Name = "App", Manufacturer = "M", Version = new Version(1, 0, 0),
            DialogSet = set,
            DialogCustomization = new DialogCustomization()
                .InsertStep("FirstStep", DialogStepAnchor.BeforeInstall)
                .InsertStep("LastStep", DialogStepAnchor.BeforeInstall).ToModel()
        };
        var context = new RecipeBuildContext(
            new ResolvedPackage { Package = package, Components = [], Files = [] },
            new DictionaryStreamRegistry());
        var result = new DialogSetProducer(
            [new NamedProbeStepBuilder("FirstStep"), new NamedProbeStepBuilder("LastStep")]).Produce(context);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var events = result.Value.First(t => t.Name.Value == "ControlEvent").Rows;
        var directRoutes = events.Where(r => Str(r.Cells[0]) == "SetupTypeDlg"
            && Str(r.Cells[2]) == "NewDialog" && Str(r.Cells[3]) == "FirstStep").ToArray();
        Assert.Equal(2, directRoutes.Length);
        Assert.Contains(events, r => Str(r.Cells[0]) == "FirstStep"
            && Str(r.Cells[2]) == "NewDialog" && Str(r.Cells[3]) == "LastStep");
        Assert.Contains(events, r => Str(r.Cells[0]) == "LastStep"
            && Str(r.Cells[2]) == "EndDialog" && Str(r.Cells[3]) == "Return");
    }

    [Theory]
    [InlineData(true, "EndDialog", "Return", false)]
    [InlineData(true, " enddialog ", " return ", false)]
    [InlineData(false, "EndDialog", "Return", true)]
    [InlineData(true, "EndDialog", "Exit", true)]
    public void Custom_install_handoffs_cannot_bypass_before_install(
        bool insertStep, string eventName, string argument, bool accepted)
    {
        var package = new PackageModel
        {
            Name = "App", Manufacturer = "M", Version = new Version(1, 0, 0),
            DialogSet = MsiDialogSet.Minimal,
            DialogCustomization = insertStep
                ? new DialogCustomization().InsertStep("ProbeStep", DialogStepAnchor.BeforeInstall).ToModel()
                : null,
            CustomDialogs =
            [
                new CustomDialogModel
                {
                    Id = "CustomPage",
                    Controls =
                    [
                        new CustomDialogControlModel
                        {
                            Name = "Go", Type = CustomControlType.PushButton, Width = 66, Height = 17,
                            Events = [new CustomDialogControlEventModel { Event = eventName, Argument = argument }]
                        }
                    ]
                }
            ]
        };
        var context = new RecipeBuildContext(
            new ResolvedPackage { Package = package, Components = [], Files = [] },
            new DictionaryStreamRegistry());
        var result = new DialogSetProducer([new ProbeStepBuilder()]).Produce(context);
        Assert.Equal(accepted, result.IsSuccess);
        if (!accepted)
        {
            Assert.Contains("DLG029", result.Error.Message, StringComparison.Ordinal);
            Assert.Contains("CustomPage", result.Error.Message, StringComparison.Ordinal);
        }
    }

    private sealed class NamedProbeStepBuilder(
        string name, string? emittedName = null, string? siblingCondition = null, int? siblingOrder = null) : IMsiDialogStepBuilder
    {
        public string Name => name;

        public MsiDialogModel Build(DialogBuildContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            DialogControlEvent next = DialogFooter.NextEvent(context.Flow);
            var model = new MsiDialogModel { Name = emittedName ?? name, FirstControl = "Go" };
            model.Controls.Add(new MsiControlModel
            {
                Name = "Go", Type = MsiControlType.PushButton,
                X = 280, Y = 240, Width = 66, Height = 17, Text = "Go",
            });
            model.Events.Add(new MsiControlEventModel
            {
                DialogName = name,
                ControlName = "Go",
                Event = MsiControlEvent.Parse(next.Event),
                Argument = next.Argument,
                Ordering = 2,
            });
            if (siblingOrder is { } order)
                model.Events.Add(new MsiControlEventModel
                {
                    DialogName = model.Name, ControlName = "Go",
                    Event = MsiControlEvent.Parse("[PROBE]"), Argument = "1",
                    Condition = siblingCondition, Ordering = order
                });
            return model;
        }
    }
}
