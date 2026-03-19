using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class InstallDirDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            SharedDialogBuilders.BuildWelcomeDlg(nextDialog: "LicenseAgreementDlg"),
            SharedDialogBuilders.BuildLicenseAgreementDlg(
                backDialog: "WelcomeDlg",
                nextDialog: "InstallDirDlg",
                includeDescription: true),
            SharedDialogBuilders.BuildInstallDirDlg(backDialog: "LicenseAgreementDlg"),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: true),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }
}
