using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class AdvancedDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            SharedDialogBuilders.BuildWelcomeDlg(nextDialog: "InstallScopeDlg"),
            BuildInstallScopeDlg(),
            SharedDialogBuilders.BuildLicenseAgreementDlg(
                backDialog: "InstallScopeDlg",
                nextDialog: "SetupTypeDlg"),
            SharedDialogBuilders.BuildSetupTypeDlg(includeDescriptions: false),
            SharedDialogBuilders.BuildCustomizeDlg(
                backDialog: "SetupTypeDlg",
                includeDescription: false),
            SharedDialogBuilders.BuildInstallDirDlg(
                backDialog: "SetupTypeDlg",
                includeDescription: false),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: false),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }

    private static MsiDialogModel BuildInstallScopeDlg()
    {
        var dlg = "InstallScopeDlg";
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
                    Type = "Text",
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = 196611,
                    Text = "{\\DlgFontBold8}!(loc.Dialog.InstallScope.Title)"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = "Text",
                    X = 25, Y = 23, Width = 280, Height = 20,
                    Attributes = 196611,
                    Text = "!(loc.Dialog.InstallScope.Description)"
                },
                new MsiControlModel
                {
                    Name = "PerMachine",
                    Type = "PushButton",
                    X = 40, Y = 75, Width = 290, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Dialog.InstallScope.AllUsers)",
                    NextControl = "PerUser"
                },
                new MsiControlModel
                {
                    Name = "PerMachineDesc",
                    Type = "Text",
                    X = 60, Y = 95, Width = 270, Height = 20,
                    Attributes = 196611,
                    Text = "!(loc.Dialog.InstallScope.AllUsersDesc)"
                },
                new MsiControlModel
                {
                    Name = "PerUser",
                    Type = "PushButton",
                    X = 40, Y = 125, Width = 290, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Dialog.InstallScope.CurrentUser)",
                    NextControl = "Back"
                },
                new MsiControlModel
                {
                    Name = "PerUserDesc",
                    Type = "Text",
                    X = 60, Y = 145, Width = 270, Height = 20,
                    Attributes = 196611,
                    Text = "!(loc.Dialog.InstallScope.CurrentUserDesc)"
                },
                new MsiControlModel
                {
                    Name = "BottomLine",
                    Type = "Line",
                    X = 0, Y = 234, Width = 370, Height = 0,
                    Attributes = 1
                },
                new MsiControlModel
                {
                    Name = "Back",
                    Type = "PushButton",
                    X = 180, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Button.Back)",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
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
                    Event = "NewDialog",
                    Argument = "WelcomeDlg",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "PerMachine",
                    Event = "[ALLUSERS]",
                    Argument = "1",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "PerMachine",
                    Event = "NewDialog",
                    Argument = "LicenseAgreementDlg",
                    Ordering = 2
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "PerUser",
                    Event = "[ALLUSERS]",
                    Argument = "{}",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "PerUser",
                    Event = "NewDialog",
                    Argument = "LicenseAgreementDlg",
                    Ordering = 2
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Cancel",
                    Event = "SpawnDialog",
                    Argument = "CancelDlg",
                    Ordering = 1
                }
            ]
        };
    }
}
