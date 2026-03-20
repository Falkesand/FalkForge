using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class MondoDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            SharedDialogBuilders.BuildWelcomeDlg(nextDialog: DialogNames.LicenseAgreement),
            SharedDialogBuilders.BuildLicenseAgreementDlg(
                backDialog: DialogNames.Welcome,
                nextDialog: DialogNames.SetupType),
            SharedDialogBuilders.BuildSetupTypeDlg(includeDescriptions: true),
            SharedDialogBuilders.BuildCustomizeDlg(backDialog: DialogNames.SetupType),
            SharedDialogBuilders.BuildInstallDirDlg(
                backDialog: DialogNames.SetupType,
                includeDescription: false),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: false),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }
}
