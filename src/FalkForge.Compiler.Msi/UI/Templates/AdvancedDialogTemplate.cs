using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

/// <summary>
/// Advanced dialog template: Welcome → InstallScope → License → SetupType → (Customize /
/// InstallDir) → Progress → Exit, plus the Cancel and Browse support modals.
/// </summary>
/// <remarks>
/// Phase 10 of the dialog deepening RFC: composes via <see cref="DialogComposer"/> and the
/// stock layout-based builders, including the new <see cref="InstallScopeDlgBuilder"/> for
/// the per-machine vs. per-user scope dialog. The template now also emits the
/// <c>CancelDlg</c> and <c>BrowseDlg</c> support dialogs.
/// </remarks>
internal sealed class AdvancedDialogTemplate : IDialogTemplate
{
    /// <inheritdoc />
    public ImmutableArray<string> StockChain => [DialogNames.Welcome, DialogNames.InstallScope, DialogNames.LicenseAgreement, DialogNames.SetupType, DialogNames.Customize];

    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var customization = package.DialogCustomization;
        var layout = Layouts.Standard370x270;

        // Extension steps are spliced into the chain BEFORE anything is composed, so the
        // button labels follow from the resulting flow instead of being patched afterwards.
        var flow = DialogFlowSplice.Resolve(StockChain, customization);

        return
        [
            DialogComposer.Compose(
                WelcomeDlgBuilder.Build(flow.FlowFor(DialogNames.Welcome)),
                layout,
                customization),
            DialogComposer.Compose(
                InstallScopeDlgBuilder.Build(flow.FlowFor(DialogNames.InstallScope)),
                layout,
                customization),
            DialogComposer.Compose(
                LicenseDlgBuilder.Build(flow.FlowFor(DialogNames.LicenseAgreement)),
                layout,
                customization),
            DialogComposer.Compose(
                SetupTypeDlgBuilder.Build(flow.FlowFor(DialogNames.SetupType)),
                layout,
                customization),
            DialogComposer.Compose(
                CustomizeDlgBuilder.Build(flow.FlowFor(DialogNames.Customize)),
                layout,
                customization),
            DialogComposer.Compose(
                // Off the wizard chain: this set composes InstallDirDlg but never navigates
                // to it, so it takes a literal flow rather than a spliced one and a step can
                // never be anchored to it here. Its Back is authored for the day something does
                // navigate to it.
                InstallDirDlgBuilder.Build(new DialogFlowContext
                {
                    BackDialog = DialogNames.SetupType,
                }),
                layout,
                customization),
            DialogComposer.Compose(
                ProgressDlgBuilder.Build(new DialogFlowContext { IncludeStatusLabel = false }),
                layout,
                customization),
            DialogComposer.Compose(
                ExitDlgBuilder.Build(),
                layout,
                customization),
            // Support dialogs (spawned by other dialogs, not in sequence)
            DialogComposer.Compose(
                CancelDlgBuilder.Build(),
                layout,
                customization),
            DialogComposer.Compose(
                BrowseDlgBuilder.Build(),
                layout,
                customization),
        ];
    }
}
