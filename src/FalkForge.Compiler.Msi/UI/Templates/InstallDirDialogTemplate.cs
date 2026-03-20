using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class InstallDirDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            SharedDialogBuilders.BuildWelcomeDlg(nextDialog: DialogNames.LicenseAgreement),
            SharedDialogBuilders.BuildLicenseAgreementDlg(
                backDialog: DialogNames.Welcome,
                nextDialog: DialogNames.InstallDir,
                includeDescription: true),
            SharedDialogBuilders.BuildInstallDirDlg(backDialog: DialogNames.LicenseAgreement),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: true),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }
}
