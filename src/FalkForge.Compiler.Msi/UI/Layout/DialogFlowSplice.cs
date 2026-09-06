using System.Collections.Immutable;

using FalkForge.Compiler.Msi.UI.Templates;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Computes the wizard chain for a dialog set once extension steps named by
/// <see cref="DialogCustomizationModel.InsertedSteps"/> have been spliced into it, and hands each
/// dialog the <see cref="DialogFlowContext"/> that follows from its position in that chain.
/// </summary>
/// <remarks>
/// <para>
/// This runs BEFORE any dialog is composed, and that ordering is the whole point.
/// <see cref="Builders.DialogFooter.NextButton"/> derives a button's label from the event it fires,
/// during composition. Splicing afterwards by rewriting control events leaves the labels behind:
/// the dialog that used to start the install keeps reading "Install" while it now only advances a
/// page, and the inserted step reads "Next" while it actually starts the install. Both were
/// measured. Splicing first means the labels, the tab order and everything else downstream fall out
/// of the same single decision.
/// </para>
/// <para>
/// The chain holds only the interactive wizard pages, from the first page to the last page before
/// the install. ProgressDlg, ExitDlg, CancelDlg, BrowseDlg and MsiRMFilesInUse are not pages the
/// user walks through and never appear in it.
/// </para>
/// </remarks>
internal sealed class DialogFlowSplice
{
    private readonly ImmutableArray<string> _chain;

    private DialogFlowSplice(ImmutableArray<string> chain) => _chain = chain;

    /// <summary>The spliced chain, first page first.</summary>
    public ImmutableArray<string> Chain => _chain;

    /// <summary>
    /// Splices every inserted step into <paramref name="stockChain"/> at its anchor.
    /// </summary>
    /// <param name="stockChain">The template's own chain, before any insertion.</param>
    /// <param name="customization">The active customization, or null when there is none.</param>
    public static DialogFlowSplice Resolve(
        ImmutableArray<string> stockChain,
        DialogCustomizationModel? customization)
    {
        if (customization is null || customization.InsertedSteps.IsDefaultOrEmpty)
        {
            return new DialogFlowSplice(stockChain);
        }

        var chain = stockChain.ToBuilder();

        // Call order, not anchor order. DialogCustomization freezes a List<> and nothing sorts it,
        // so two steps after one anchor appear in the order the author wrote them.
        foreach (InsertedDialogStep step in customization.InsertedSteps)
        {
            int at = AnchorIndex(chain, step.After);
            if (at < 0)
            {
                // The anchor is not in this set. Left alone here and reported by the producer,
                // which can name the dialog set in the message.
                continue;
            }

            // Insert AFTER the anchor, and after any step already placed at the same anchor, so
            // that two steps sharing an anchor keep their call order rather than reversing.
            int insertAt = at + 1;
            while (insertAt < chain.Count && !stockChain.Contains(chain[insertAt]))
            {
                insertAt++;
            }

            chain.Insert(insertAt, step.StepName);
        }

        return new DialogFlowSplice(chain.ToImmutable());
    }

    /// <summary>
    /// True when <paramref name="anchor"/> names a dialog present in this set's stock chain.
    /// </summary>
    public static bool AnchorIsPresent(ImmutableArray<string> stockChain, DialogStepAnchor anchor) =>
        anchor == DialogStepAnchor.BeforeInstall || stockChain.Contains(AnchorDialogName(anchor));

    /// <summary>
    /// The flow context for <paramref name="dialogName"/> given its position in the spliced chain.
    /// The last page's <see cref="DialogFlowContext.NextDialog"/> is the progress dialog, which
    /// <see cref="Builders.DialogFooter.NextEvent"/> turns into the EndDialog/Return handoff and
    /// <see cref="Builders.DialogFooter.NextButton"/> labels "Install".
    /// </summary>
    public DialogFlowContext FlowFor(string dialogName)
    {
        int i = _chain.IndexOf(dialogName);
        if (i < 0)
        {
            return new DialogFlowContext();
        }

        return new DialogFlowContext
        {
            BackDialog = i > 0 ? _chain[i - 1] : null,
            NextDialog = i < _chain.Length - 1 ? _chain[i + 1] : DialogNames.Progress,
        };
    }

    /// <summary>
    /// Where a control that starts the install should now point. Normally the progress dialog,
    /// which means "hand off to InstallUISequence". When a step is anchored at
    /// <see cref="DialogStepAnchor.BeforeInstall"/>, every such control routes through that step
    /// first instead, which is what makes the anchor well defined on the branching sets where more
    /// than one dialog can start the install.
    /// </summary>
    public string InstallTarget =>
        _chain.IsDefaultOrEmpty ? DialogNames.Progress : _chain[^1];

    private static int AnchorIndex(ImmutableArray<string>.Builder chain, DialogStepAnchor anchor)
    {
        if (anchor == DialogStepAnchor.BeforeInstall)
        {
            return chain.Count - 1;
        }

        return chain.IndexOf(AnchorDialogName(anchor));
    }

    private static string AnchorDialogName(DialogStepAnchor anchor) => anchor switch
    {
        DialogStepAnchor.Welcome => DialogNames.Welcome,
        DialogStepAnchor.License => DialogNames.LicenseAgreement,
        DialogStepAnchor.InstallScope => DialogNames.InstallScope,
        DialogStepAnchor.InstallDir => DialogNames.InstallDir,
        DialogStepAnchor.Features => DialogNames.Customize,
        _ => string.Empty,
    };
}
