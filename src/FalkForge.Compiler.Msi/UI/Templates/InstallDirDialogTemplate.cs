using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

/// <summary>
/// InstallDir dialog template: Welcome → License → InstallDir → Progress → Exit, plus the
/// Cancel and Browse support modals.
/// </summary>
/// <remarks>
/// Phase 10 of the dialog deepening RFC: the template now composes its dialogs via
/// <see cref="DialogComposer"/> against the layout-based stock builders rather than calling
/// the legacy hand-coded <c>SharedDialogBuilders</c> entry points.
/// </remarks>
internal sealed class InstallDirDialogTemplate : IDialogTemplate
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
                    NextDialog = DialogNames.InstallDir,
                }),
                layout,
                customization),
            DialogComposer.Compose(
                InstallDirDlgBuilder.Build(new DialogFlowContext
                {
                    BackDialog = DialogNames.LicenseAgreement,
                }),
                layout,
                customization),
            DialogComposer.Compose(
                ProgressDlgBuilder.Build(new DialogFlowContext { IncludeStatusLabel = true }),
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
