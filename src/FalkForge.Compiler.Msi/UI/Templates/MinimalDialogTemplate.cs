using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

/// <summary>
/// Minimal dialog template: Welcome → Progress → Exit, plus the Cancel confirmation modal.
/// </summary>
/// <remarks>
/// Phase 10 of the dialog deepening RFC: this template now composes its dialogs via
/// <see cref="DialogComposer"/> against the stock layout-based builders rather than
/// calling the legacy hand-coded <c>SharedDialogBuilders</c> entry points. The dialog
/// set is also expanded to include the <c>CancelDlg</c> modal (previously referenced
/// via SpawnDialog from Welcome but never emitted — a pre-existing template bug).
/// </remarks>
internal sealed class MinimalDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var customization = package.DialogCustomization;
        var layout = Layouts.Standard370x270;

        return
        [
            DialogComposer.Compose(
                WelcomeDlgBuilder.Build(new DialogFlowContext { NextDialog = DialogNames.Progress }),
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
            DialogComposer.Compose(
                CancelDlgBuilder.Build(),
                layout,
                customization),
        ];
    }
}
