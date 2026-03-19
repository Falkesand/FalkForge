using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class FeatureTreeDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            SharedDialogBuilders.BuildWelcomeDlg(nextDialog: "LicenseAgreementDlg"),
            SharedDialogBuilders.BuildLicenseAgreementDlg(
                backDialog: "WelcomeDlg",
                nextDialog: "CustomizeDlg"),
            SharedDialogBuilders.BuildCustomizeDlg(backDialog: "LicenseAgreementDlg"),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: false),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }
}
