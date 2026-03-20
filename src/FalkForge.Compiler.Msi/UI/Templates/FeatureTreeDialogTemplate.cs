using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class FeatureTreeDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            SharedDialogBuilders.BuildWelcomeDlg(nextDialog: DialogNames.LicenseAgreement),
            SharedDialogBuilders.BuildLicenseAgreementDlg(
                backDialog: DialogNames.Welcome,
                nextDialog: DialogNames.Customize),
            SharedDialogBuilders.BuildCustomizeDlg(backDialog: DialogNames.LicenseAgreement),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: false),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }
}
