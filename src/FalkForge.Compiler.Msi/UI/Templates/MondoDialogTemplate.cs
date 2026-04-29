using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

/// <summary>
/// Mondo dialog template: Welcome → License → SetupType → (Customize / InstallDir) → Progress
/// → Exit, plus the Cancel and Browse support modals.
/// </summary>
/// <remarks>
/// Phase 10 of the dialog deepening RFC: composes via <see cref="DialogComposer"/> and the
/// stock layout-based builders. The template now also emits the <c>CancelDlg</c> and
/// <c>BrowseDlg</c> support dialogs that the wizard SpawnDialog events reference.
/// </remarks>
internal sealed class MondoDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var customization = package.DialogCustomization;
        var layout = Layouts.Standard370x270;

        return
        [
            DialogComposer.Compose(
                WelcomeDlgBuilder.Build(new DialogFlowContext { NextDialog = DialogNames.LicenseAgreement }),
                layout,
                customization),
            DialogComposer.Compose(
                LicenseDlgBuilder.Build(new DialogFlowContext
                {
                    BackDialog = DialogNames.Welcome,
                    NextDialog = DialogNames.SetupType,
                }),
                layout,
                customization),
            DialogComposer.Compose(
                SetupTypeDlgBuilder.Build(new DialogFlowContext
                {
                    BackDialog = DialogNames.LicenseAgreement,
                }),
                layout,
                customization),
            DialogComposer.Compose(
                CustomizeDlgBuilder.Build(new DialogFlowContext
                {
                    BackDialog = DialogNames.SetupType,
                    NextDialog = DialogNames.Progress,
                }),
                layout,
                customization),
            DialogComposer.Compose(
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
