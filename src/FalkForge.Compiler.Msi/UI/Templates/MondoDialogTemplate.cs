using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class MondoDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            SharedDialogBuilders.BuildWelcomeDlg(nextDialog: "LicenseAgreementDlg"),
            SharedDialogBuilders.BuildLicenseAgreementDlg(
                backDialog: "WelcomeDlg",
                nextDialog: "SetupTypeDlg"),
            SharedDialogBuilders.BuildSetupTypeDlg(includeDescriptions: true),
            SharedDialogBuilders.BuildCustomizeDlg(backDialog: "SetupTypeDlg"),
            SharedDialogBuilders.BuildInstallDirDlg(
                backDialog: "SetupTypeDlg",
                includeDescription: false),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: false),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }
}
