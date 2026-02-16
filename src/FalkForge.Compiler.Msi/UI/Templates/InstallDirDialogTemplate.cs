using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class InstallDirDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            BuildWelcomeDlg(),
            BuildLicenseAgreementDlg(),
            BuildInstallDirDlg(),
            BuildProgressDlg(),
            BuildExitDlg()
        ];
    }

    private static MsiDialogModel BuildWelcomeDlg()
    {
        var dlg = "WelcomeDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "Next",
            DefaultControl = "Next",
            CancelControl = "Cancel",
            Controls =
            [
                new MsiControlModel
                {
                    Name = "Title",
                    Type = "Text",
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = 196611,
                    Text = "{\\DlgFontBold8}Welcome to [ProductName]"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = "Text",
                    X = 25, Y = 23, Width = 280, Height = 40,
                    Attributes = 196611,
                    Text = "The Setup Wizard will install [ProductName] on your computer. Click Next to continue or Cancel to exit the Setup Wizard."
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
                    Name = "Next",
                    Type = "PushButton",
                    X = 236, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "&Next >",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "Cancel",
                    NextControl = "Next"
                }
            ],
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Next",
                    Event = "NewDialog",
                    Argument = "LicenseAgreementDlg",
                    Ordering = 1
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

    private static MsiDialogModel BuildLicenseAgreementDlg()
    {
        var dlg = "LicenseAgreementDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "LicenseText",
            DefaultControl = "Next",
            CancelControl = "Cancel",
            Controls =
            [
                new MsiControlModel
                {
                    Name = "Title",
                    Type = "Text",
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = 196611,
                    Text = "{\\DlgFontBold8}License Agreement"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = "Text",
                    X = 25, Y = 23, Width = 280, Height = 15,
                    Attributes = 196611,
                    Text = "Please read the following license agreement."
                },
                new MsiControlModel
                {
                    Name = "LicenseText",
                    Type = "ScrollableText",
                    X = 20, Y = 60, Width = 330, Height = 140,
                    Attributes = 7, // Visible | Enabled | Sunken
                    Property = "LicenseText",
                    NextControl = "LicenseAccepted"
                },
                new MsiControlModel
                {
                    Name = "LicenseAccepted",
                    Type = "CheckBox",
                    X = 20, Y = 207, Width = 330, Height = 18,
                    Attributes = 3,
                    Property = "LicenseAccepted",
                    Text = "I &accept the terms in the License Agreement",
                    NextControl = "Back"
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
                    Text = "< &Back",
                    NextControl = "Next"
                },
                new MsiControlModel
                {
                    Name = "Next",
                    Type = "PushButton",
                    X = 236, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "&Next >",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "Cancel",
                    NextControl = "LicenseText"
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
                    ControlName = "Next",
                    Event = "NewDialog",
                    Argument = "InstallDirDlg",
                    Condition = "LicenseAccepted = \"1\"",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Cancel",
                    Event = "SpawnDialog",
                    Argument = "CancelDlg",
                    Ordering = 1
                }
            ],
            Conditions =
            [
                new MsiControlConditionModel
                {
                    DialogName = dlg,
                    ControlName = "Next",
                    Action = "Disable",
                    Condition = "NOT LicenseAccepted = \"1\""
                },
                new MsiControlConditionModel
                {
                    DialogName = dlg,
                    ControlName = "Next",
                    Action = "Enable",
                    Condition = "LicenseAccepted = \"1\""
                }
            ]
        };
    }

    private static MsiDialogModel BuildInstallDirDlg()
    {
        var dlg = "InstallDirDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "Folder",
            DefaultControl = "Next",
            CancelControl = "Cancel",
            Controls =
            [
                new MsiControlModel
                {
                    Name = "Title",
                    Type = "Text",
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = 196611,
                    Text = "{\\DlgFontBold8}Destination Folder"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = "Text",
                    X = 25, Y = 23, Width = 280, Height = 15,
                    Attributes = 196611,
                    Text = "Click Next to install to this folder, or click Change to install to a different folder."
                },
                new MsiControlModel
                {
                    Name = "FolderLabel",
                    Type = "Text",
                    X = 20, Y = 60, Width = 290, Height = 15,
                    Attributes = 3,
                    Text = "Install [ProductName] to:"
                },
                new MsiControlModel
                {
                    Name = "Folder",
                    Type = "PathEdit",
                    X = 20, Y = 80, Width = 260, Height = 18,
                    Attributes = 3,
                    Property = "WIXUI_INSTALLDIR",
                    NextControl = "ChangeFolder"
                },
                new MsiControlModel
                {
                    Name = "ChangeFolder",
                    Type = "PushButton",
                    X = 284, Y = 80, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "C&hange...",
                    NextControl = "Back"
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
                    Text = "< &Back",
                    NextControl = "Next"
                },
                new MsiControlModel
                {
                    Name = "Next",
                    Type = "PushButton",
                    X = 236, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "&Next >",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "Cancel",
                    NextControl = "Folder"
                }
            ],
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "ChangeFolder",
                    Event = "SpawnDialog",
                    Argument = "BrowseDlg",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Back",
                    Event = "NewDialog",
                    Argument = "LicenseAgreementDlg",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Next",
                    Event = "NewDialog",
                    Argument = "ProgressDlg",
                    Ordering = 1
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

    private static MsiDialogModel BuildProgressDlg()
    {
        var dlg = "ProgressDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            Attributes = 5,
            FirstControl = "Cancel",
            DefaultControl = "Cancel",
            CancelControl = "Cancel",
            Controls =
            [
                new MsiControlModel
                {
                    Name = "Title",
                    Type = "Text",
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = 196611,
                    Text = "{\\DlgFontBold8}Installing [ProductName]"
                },
                new MsiControlModel
                {
                    Name = "StatusLabel",
                    Type = "Text",
                    X = 25, Y = 55, Width = 50, Height = 10,
                    Attributes = 3,
                    Text = "Status:"
                },
                new MsiControlModel
                {
                    Name = "ActionText",
                    Type = "Text",
                    X = 75, Y = 55, Width = 270, Height = 10,
                    Attributes = 3
                },
                new MsiControlModel
                {
                    Name = "ProgressBar",
                    Type = "ProgressBar",
                    X = 25, Y = 70, Width = 320, Height = 10,
                    Attributes = 65539
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
                    Name = "Cancel",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "Cancel"
                }
            ],
            Events =
            [
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

    private static MsiDialogModel BuildExitDlg()
    {
        var dlg = "ExitDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "Finish",
            DefaultControl = "Finish",
            CancelControl = "Finish",
            Controls =
            [
                new MsiControlModel
                {
                    Name = "Title",
                    Type = "Text",
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = 196611,
                    Text = "{\\DlgFontBold8}Setup Complete"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = "Text",
                    X = 25, Y = 23, Width = 280, Height = 20,
                    Attributes = 196611,
                    Text = "[ProductName] has been successfully installed."
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
                    Name = "Finish",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "&Finish"
                }
            ],
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Finish",
                    Event = "EndDialog",
                    Argument = "Return",
                    Ordering = 1
                }
            ]
        };
    }
}
