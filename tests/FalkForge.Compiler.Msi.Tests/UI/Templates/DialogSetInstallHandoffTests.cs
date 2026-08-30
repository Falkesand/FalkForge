using System;
using System.Collections.Generic;
using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Templates;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Templates;

/// <summary>
/// Pins the InstallUISequence handoff contract across every stock dialog set: the
/// dialog immediately preceding ProgressDlg must end its own modal chain with
/// <c>EndDialog</c>/<c>Return</c>, handing control back to InstallUISequence so ProgressDlg
/// (1200, modeless) and ExecuteAction (1300) can run. A control that instead opens ProgressDlg
/// with <c>NewDialog</c> nests it inside the still-running dialog action, and the sequence never
/// reaches ExecuteAction, so the install never starts.
/// </summary>
/// <remarks>
/// Only <c>InstallDirDlgBuilder</c> got this right from the start. Minimal, FeatureTree, Mondo
/// and Advanced all inherited the NewDialog-to-Progress bug. The InstallDir template was fixed
/// once before while the other four templates were left behind, so this test covers every
/// stock template rather than one.
/// </remarks>
public sealed class DialogSetInstallHandoffTests
{
    // Support modals spawned outside the wizard's main dialog chain. They legitimately end with
    // EndDialog/Return (Exit's Finish button, Browse's OK button) for reasons unrelated to
    // starting the install, so they are excluded from the "some dialog hands off to Progress"
    // assertion below. Including them would let that assertion pass even when the real
    // pre-install dialog still (wrongly) uses NewDialog.
    private static readonly HashSet<string> NonHandoffDialogs = ["ExitDlg", "CancelDlg", "BrowseDlg"];

    // xUnit theory data must be public, but every template type here is internal (UI namespace
    // convention), so the template names are what cross the [MemberData] boundary and
    // ComposeByName resolves them back to a template instance.
    public static IEnumerable<object[]> AllTemplateNames()
    {
        yield return new object[] { "Minimal" };
        yield return new object[] { "FeatureTree" };
        yield return new object[] { "Mondo" };
        yield return new object[] { "Advanced" };
        yield return new object[] { "InstallDir" };
    }

    private static IReadOnlyList<MsiDialogModel> ComposeByName(string templateName)
    {
        IDialogTemplate template = templateName switch
        {
            "Minimal" => new MinimalDialogTemplate(),
            "FeatureTree" => new FeatureTreeDialogTemplate(),
            "Mondo" => new MondoDialogTemplate(),
            "Advanced" => new AdvancedDialogTemplate(),
            "InstallDir" => new InstallDirDialogTemplate(),
            _ => throw new ArgumentOutOfRangeException(nameof(templateName), templateName, "Unknown dialog template."),
        };

        return template.GetDialogs(new PackageModel
        {
            Name = "Test",
            Manufacturer = "Acme",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.Parse("12345678-1234-1234-1234-123456789abc"),
        });
    }

    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void No_control_event_navigates_into_progress_with_NewDialog(string templateName)
    {
        var dialogs = ComposeByName(templateName);

        var badRoutes = dialogs
            .SelectMany(d => d.Events)
            .Where(e => e.Event.ToString() == "NewDialog" && e.Argument == "ProgressDlg")
            .ToArray();

        Assert.Empty(badRoutes);
    }

    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void Some_pre_install_dialog_hands_off_to_InstallUISequence_with_EndDialog_Return(string templateName)
    {
        var dialogs = ComposeByName(templateName);

        var handoffEvents = dialogs
            .Where(d => !NonHandoffDialogs.Contains(d.Name))
            .SelectMany(d => d.Events)
            .Where(e => e.Event.ToString() == "EndDialog" && e.Argument == "Return")
            .ToArray();

        Assert.NotEmpty(handoffEvents);
    }

    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void Progress_dialog_is_not_modal(string templateName)
    {
        // ProgressDlg has no control that fires EndDialog, because the user is not meant to
        // dismiss it. Authored modal, Windows Installer runs it as a blocking message loop that
        // waits for an EndDialog which can never arrive, so InstallUISequence never advances to
        // ExecuteAction at 1300 and the install never starts. Authored modeless, it paints and
        // returns immediately and the sequence carries on.
        var progress = ComposeByName(templateName).Single(d => d.Name == "ProgressDlg");

        Assert.False(progress.Attributes.HasFlag(MsiDialogAttributes.Modal));
        Assert.True(progress.Attributes.HasFlag(MsiDialogAttributes.Visible));
    }

    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void Every_dialog_except_progress_stays_modal(string templateName)
    {
        // Guards the fix above from being applied too widely. The wizard pages must stay modal;
        // a modeless one would let the sequence run past it without waiting for the user.
        var others = ComposeByName(templateName)
            .Where(d => d.Name != "ProgressDlg")
            .ToArray();

        Assert.NotEmpty(others);
        Assert.All(others, d => Assert.True(
            d.Attributes.HasFlag(MsiDialogAttributes.Modal),
            $"{d.Name} is not modal"));
    }

    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void The_button_that_starts_the_install_is_labelled_Install(string templateName)
    {
        // "Next" tells the user another page follows and the decision is still reversible. On the
        // dialog that hands off to InstallUISequence it is not: the click starts writing files.
        // The label is derived from the event the button fires, so the two cannot disagree.
        var dialogs = ComposeByName(templateName);
        var installButtons = 0;

        foreach (var dialog in dialogs)
        {
            var next = dialog.Events.SingleOrDefault(e => e.ControlName == "Next");
            if (next is null)
            {
                continue;
            }

            var startsInstall = next.Event.ToString() == "EndDialog" && next.Argument == "Return";
            var expected = startsInstall ? "!(loc.Button.Install)" : "!(loc.Button.Next)";
            var button = dialog.Controls.Single(c => c.Name == "Next");

            Assert.Equal(expected, button.Text);
            if (startsInstall)
            {
                installButtons++;
            }
        }

        // Not a vacuous pass: every stock set has at least one dialog that starts the install.
        // Not "exactly one": Mondo and Advanced compose both CustomizeDlg and InstallDirDlg, and
        // both wire their Next to the handoff, so they report two. That second dialog is composed
        // but never navigated to, which is a separate open defect about reachability. Asserting
        // exactly one here would bake that defect's absence into this test and fail for a reason
        // that has nothing to do with button labels.
        Assert.True(installButtons >= 1, $"{templateName} has no dialog that starts the install");
    }
}
