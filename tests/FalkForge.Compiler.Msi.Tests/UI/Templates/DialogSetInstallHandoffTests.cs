using System;
using System.Collections.Generic;
using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Templates;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Templates;

/// <summary>
/// Pins the InstallUISequence handoff contract across every stock dialog set (ledger D64): the
/// dialog immediately preceding ProgressDlg must end its own modal chain with
/// <c>EndDialog</c>/<c>Return</c>, handing control back to InstallUISequence so ProgressDlg
/// (1200, modeless) and ExecuteAction (1300) can run. A control that instead opens ProgressDlg
/// with <c>NewDialog</c> nests it inside the still-running dialog action, and the sequence never
/// reaches ExecuteAction, so the install never starts.
/// </summary>
/// <remarks>
/// Only <c>InstallDirDlgBuilder</c> got this right from the start. Minimal, FeatureTree, Mondo
/// and Advanced all inherited the NewDialog-to-Progress bug. The InstallDir template was fixed
/// once before (see memory <c>project_msi_dialog_template_gap</c>) while the other four templates
/// were left behind, so this test covers every stock template rather than one.
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
}
