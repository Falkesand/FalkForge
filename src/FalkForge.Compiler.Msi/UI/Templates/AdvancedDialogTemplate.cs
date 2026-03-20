using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class AdvancedDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            SharedDialogBuilders.BuildWelcomeDlg(nextDialog: DialogNames.InstallScope),
            BuildInstallScopeDlg(),
            SharedDialogBuilders.BuildLicenseAgreementDlg(
                backDialog: DialogNames.InstallScope,
                nextDialog: DialogNames.SetupType),
            SharedDialogBuilders.BuildSetupTypeDlg(includeDescriptions: false),
            SharedDialogBuilders.BuildCustomizeDlg(
                backDialog: DialogNames.SetupType,
                includeDescription: false),
            SharedDialogBuilders.BuildInstallDirDlg(
                backDialog: DialogNames.SetupType,
                includeDescription: false),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: false),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }

    private static MsiDialogModel BuildInstallScopeDlg()
    {
        var dlg = DialogNames.InstallScope;
        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "PerMachine",
            DefaultControl = "PerMachine",
            CancelControl = "Cancel",
            Controls =
            [
                new MsiControlModel
                {
                    Name = "Title",
                    Type = MsiControlType.Text,
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                    Text = "{\\DlgFontBold8}!(loc.Dialog.InstallScope.Title)"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = MsiControlType.Text,
                    X = 25, Y = 23, Width = 280, Height = 20,
                    Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                    Text = "!(loc.Dialog.InstallScope.Description)"
                },
                new MsiControlModel
                {
                    Name = "PerMachine",
                    Type = MsiControlType.PushButton,
                    X = 40, Y = 75, Width = 290, Height = 17,
                    Text = "!(loc.Dialog.InstallScope.AllUsers)",
                    NextControl = "PerUser"
                },
                new MsiControlModel
                {
                    Name = "PerMachineDesc",
                    Type = MsiControlType.Text,
                    X = 60, Y = 95, Width = 270, Height = 20,
                    Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                    Text = "!(loc.Dialog.InstallScope.AllUsersDesc)"
                },
                new MsiControlModel
                {
                    Name = "PerUser",
                    Type = MsiControlType.PushButton,
                    X = 40, Y = 125, Width = 290, Height = 17,
                    Text = "!(loc.Dialog.InstallScope.CurrentUser)",
                    NextControl = "Back"
                },
                new MsiControlModel
                {
                    Name = "PerUserDesc",
                    Type = MsiControlType.Text,
                    X = 60, Y = 145, Width = 270, Height = 20,
                    Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                    Text = "!(loc.Dialog.InstallScope.CurrentUserDesc)"
                },
                new MsiControlModel
                {
                    Name = "BottomLine",
                    Type = MsiControlType.Line,
                    X = 0, Y = 234, Width = 370, Height = 0,
                    Attributes = MsiControlAttributes.Visible
                },
                new MsiControlModel
                {
                    Name = "Back",
                    Type = MsiControlType.PushButton,
                    X = 180, Y = 243, Width = 56, Height = 17,
                    Text = "!(loc.Button.Back)",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = MsiControlType.PushButton,
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Text = "!(loc.Button.Cancel)",
                    NextControl = "PerMachine"
                }
            ],
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Back",
                    Event = MsiControlEvent.NewDialog,
                    Argument = DialogNames.Welcome,
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "PerMachine",
                    Event = MsiControlEvent.SetProperty("ALLUSERS"),
                    Argument = "1",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "PerMachine",
                    Event = MsiControlEvent.NewDialog,
                    Argument = DialogNames.LicenseAgreement,
                    Ordering = 2
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "PerUser",
                    Event = MsiControlEvent.SetProperty("ALLUSERS"),
                    Argument = "{}",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "PerUser",
                    Event = MsiControlEvent.NewDialog,
                    Argument = DialogNames.LicenseAgreement,
                    Ordering = 2
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Cancel",
                    Event = MsiControlEvent.SpawnDialog,
                    Argument = DialogNames.Cancel,
                    Ordering = 1
                }
            ]
        };
    }
}
