using System;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

/// <summary>
/// Pins the install-flow contract across the composed dialog set: stock template dialogs and
/// author-defined custom dialogs. Extension-contributed steps are spliced into the chain and so are
/// reachable, but they are covered by DialogSetProducerStepSpliceTests rather than here, along with
/// the step-specific diagnostics that catch a step which publishes no forward navigation.
/// </summary>
/// <remarks>
/// Windows Installer runs a modal dialog in InstallUISequence as a blocking message loop that
/// ends only when a control publishes EndDialog. A modal dialog the sequence schedules must
/// therefore be able to reach an EndDialog, or the sequence parks on it and ExecuteAction never
/// runs. The walk follows NewDialog edges only. SpawnDialog opens a child on top and returns
/// control to the parent when the child closes, so an EndDialog inside a spawned child ends the
/// child rather than resuming the sequence.
/// </remarks>
public sealed class DialogSetProducerInstallFlowTests
{
    private static Result<ImmutableArray<RecipeTable>> Produce(PackageModel package)
    {
        var ctx = new RecipeBuildContext(
            new ResolvedPackage { Package = package, Components = [], Files = [] },
            new DictionaryStreamRegistry());
        return new DialogSetProducer().Produce(ctx);
    }

    private static PackageModel Package(MsiDialogSet set, params CustomDialogModel[] dialogs) => new()
    {
        Name = "App",
        Manufacturer = "M",
        Version = new Version(1, 0, 0),
        DialogSet = set,
        CustomDialogs = dialogs,
    };

    private static CustomDialogControlModel Button(string name, params CustomDialogControlEventModel[] events) => new()
    {
        Name = name,
        Type = CustomControlType.PushButton,
        X = 280,
        Y = 240,
        Width = 66,
        Height = 17,
        Text = name,
        Events = events,
    };

    private static CustomDialogControlEventModel Event(string verb, string argument) =>
        new() { Event = verb, Argument = argument };

    public static TheoryData<MsiDialogSet> StockSets() =>
        [MsiDialogSet.Minimal, MsiDialogSet.FeatureTree, MsiDialogSet.Mondo, MsiDialogSet.Advanced, MsiDialogSet.InstallDir];

    [Theory]
    [MemberData(nameof(StockSets))]
    public void Every_stock_dialog_set_satisfies_the_install_flow_check(MsiDialogSet set)
    {
        // The most important negative case in this file. If the check cannot pass the sets the
        // product itself ships, it is wrong, not the sets. All five were proven to install through
        // their own UI, so a check that rejects one has a defect.
        Result<ImmutableArray<RecipeTable>> result = Produce(Package(set));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void A_custom_dialog_navigating_into_the_progress_dialog_is_rejected()
    {
        // ProgressDlg is modeless and carries no control that publishes EndDialog, by design,
        // because the user is not meant to dismiss it. Reaching it with NewDialog opens it nested
        // inside the still-running dialog action, which never returns, so ExecuteAction never
        // runs. This is the authored-path form of the defect fixed for the stock sets.
        PackageModel package = Package(
            MsiDialogSet.Minimal,
            new CustomDialogModel
            {
                Id = "PreflightDlg",
                SequenceNumber = 1100,
                Controls = [Button("Go", Event("NewDialog", "ProgressDlg"))],
            });

        Result<ImmutableArray<RecipeTable>> result = Produce(package);

        Assert.True(result.IsFailure);
        Assert.Contains("PreflightDlg", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chain_that_ends_several_hops_later_is_accepted()
    {
        // The canonical wizard. The scheduled entry dialog publishes NewDialog, not EndDialog, and
        // the chain ends further along. An earlier version of this check rejected exactly this
        // shape, which is what the product itself ships.
        PackageModel package = Package(
            MsiDialogSet.None,
            new CustomDialogModel
            {
                Id = "StepOne",
                SequenceNumber = 1100,
                Controls = [Button("Next", Event("NewDialog", "StepTwo"))],
            },
            new CustomDialogModel
            {
                Id = "StepTwo",
                Controls = [Button("Next", Event("NewDialog", "StepThree"))],
            },
            new CustomDialogModel
            {
                Id = "StepThree",
                Controls = [Button("Install", Event("EndDialog", "Return"))],
            });

        Result<ImmutableArray<RecipeTable>> result = Produce(package);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void An_EndDialog_reachable_only_through_a_spawned_child_does_not_satisfy_the_check()
    {
        // SpawnDialog opens the child on top and hands control back to the parent when the child
        // closes, so the child's EndDialog ends the child, not the parent's wait. A root whose
        // only EndDialog sits in a spawned cancel confirmation can abort the install but can never
        // start it. Following SpawnDialog edges would wave this through.
        PackageModel package = Package(
            MsiDialogSet.None,
            new CustomDialogModel
            {
                Id = "StepOne",
                SequenceNumber = 1100,
                Controls = [Button("Cancel", Event("SpawnDialog", "AreYouSureDlg"))],
            },
            new CustomDialogModel
            {
                Id = "AreYouSureDlg",
                Controls = [Button("Yes", Event("EndDialog", "Exit"))],
            });

        Result<ImmutableArray<RecipeTable>> result = Produce(package);

        Assert.True(result.IsFailure);
        Assert.Contains("StepOne", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_navigation_cycle_terminates_and_is_rejected()
    {
        // Two dialogs pointing at each other with no EndDialog anywhere. The walk must terminate
        // rather than loop, and must report the dead end.
        PackageModel package = Package(
            MsiDialogSet.None,
            new CustomDialogModel
            {
                Id = "PingDlg",
                SequenceNumber = 1100,
                Controls = [Button("Next", Event("NewDialog", "PongDlg"))],
            },
            new CustomDialogModel
            {
                Id = "PongDlg",
                Controls = [Button("Back", Event("NewDialog", "PingDlg"))],
            });

        Result<ImmutableArray<RecipeTable>> result = Produce(package);

        Assert.True(result.IsFailure);
        Assert.Contains("PingDlg", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dialog_nothing_navigates_to_is_not_checked()
    {
        // An orphan dialog row is legal MSI and hangs nothing, because the sequence never reaches
        // it. Only dialogs the sequence schedules, and what they can reach, are in scope. Treating
        // every modal dialog as a root instead flags orphans that harm nobody.
        PackageModel package = Package(
            MsiDialogSet.Minimal,
            new CustomDialogModel
            {
                Id = "OrphanDlg",
                Controls = [Button("Nowhere")],
            });

        Result<ImmutableArray<RecipeTable>> result = Produce(package);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void A_lowercase_or_padded_EndDialog_verb_still_ends_the_chain()
    {
        // Author text reaches the compiler verbatim. MsiControlEvent.Parse accepts any non-empty
        // string and normalises nothing, so the comparison must trim and ignore case. Windows
        // Installer's own matching is not what is being asserted here; over-accepting the verb is
        // the safe direction for a rule that fails the build.
        PackageModel package = Package(
            MsiDialogSet.None,
            new CustomDialogModel
            {
                Id = "StepOne",
                SequenceNumber = 1100,
                Controls = [Button("Install", Event(" enddialog ", "Return"))],
            });

        Result<ImmutableArray<RecipeTable>> result = Produce(package);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void A_root_whose_only_EndDialog_is_Exit_is_rejected()
    {
        // EndDialog Exit terminates the UI without running the install. A wizard whose only exit
        // is Exit can be closed but can never install anything, so it is the same user-visible
        // failure as a dialog that never ends. Only Return hands control back to InstallUISequence
        // so it can reach ExecuteAction, which is why DialogFooter.StartsInstall already requires
        // that exact argument.
        PackageModel package = Package(
            MsiDialogSet.None,
            new CustomDialogModel
            {
                Id = "StepOne",
                SequenceNumber = 1100,
                Controls = [Button("Close", Event("EndDialog", "Exit"))],
            });

        Result<ImmutableArray<RecipeTable>> result = Produce(package);

        Assert.True(result.IsFailure);
        Assert.Contains("StepOne", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_modal_dialog_scheduled_through_UISequence_is_checked()
    {
        // Sequence(...) is not the only way a dialog reaches InstallUISequence. UISequence adds an
        // action by name, and the producer writes any name into the table verbatim, so a dialog
        // scheduled that way is just as capable of parking the sequence.
        PackageModel package = new()
        {
            Name = "App",
            Manufacturer = "M",
            Version = new Version(1, 0, 0),
            DialogSet = MsiDialogSet.None,
            CustomDialogs =
            [
                new CustomDialogModel { Id = "HangDlg", Controls = [Button("Nothing")] },
            ],
            UISequenceActions =
            [
                new SequenceActionModel
                {
                    ActionName = "HangDlg",
                    Table = SequenceTable.InstallUISequence,
                    Position = new ActionPosition.AtNumber(1150),
                },
            ],
        };

        Result<ImmutableArray<RecipeTable>> result = Produce(package);

        Assert.True(result.IsFailure);
        Assert.Contains("HangDlg", result.Error.Message, StringComparison.Ordinal);
    }
}
