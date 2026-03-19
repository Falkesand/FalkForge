using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class AdvancedDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            BuildWelcomeDlg(),
            BuildInstallScopeDlg(),
            BuildLicenseAgreementDlg(),
            BuildSetupTypeDlg(),
            BuildCustomizeDlg(),
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
                    Text = "{\\DlgFontBold8}!(loc.Dialog.Welcome.Title)"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = "Text",
                    X = 25, Y = 23, Width = 280, Height = 40,
                    Attributes = 196611,
                    Text = "!(loc.Dialog.Welcome.DescriptionFull)"
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
                    Text = "!(loc.Button.Next)",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Button.Cancel)",
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
                    Argument = "InstallScopeDlg",
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
                    Text = "{\\DlgFontBold8}!(loc.Dialog.License.Title)"
                },
                new MsiControlModel
                {
                    Name = "LicenseText",
                    Type = "ScrollableText",
                    X = 20, Y = 60, Width = 330, Height = 140,
                    Attributes = 7,
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
                    Text = "!(loc.Dialog.License.Accept)",
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
                    Text = "!(loc.Button.Back)",
                    NextControl = "Next"
                },
                new MsiControlModel
                {
                    Name = "Next",
                    Type = "PushButton",
                    X = 236, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Button.Next)",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Button.Cancel)",
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
                    Argument = "InstallScopeDlg",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Next",
                    Event = "NewDialog",
                    Argument = "SetupTypeDlg",
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

    private static MsiDialogModel BuildSetupTypeDlg()
    {
        var dlg = "SetupTypeDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "TypicalButton",
            DefaultControl = "TypicalButton",
            CancelControl = "Cancel",
            Controls =
            [
                new MsiControlModel
                {
                    Name = "Title",
                    Type = "Text",
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = 196611,
                    Text = "{\\DlgFontBold8}!(loc.Dialog.SetupType.Title)"
                },
                new MsiControlModel
                {
                    Name = "TypicalButton",
                    Type = "PushButton",
                    X = 40, Y = 65, Width = 290, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Dialog.SetupType.Typical)",
                    NextControl = "CustomButton"
                },
                new MsiControlModel
                {
                    Name = "CustomButton",
                    Type = "PushButton",
                    X = 40, Y = 115, Width = 290, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Dialog.SetupType.Custom)",
                    NextControl = "CompleteButton"
                },
                new MsiControlModel
                {
                    Name = "CompleteButton",
                    Type = "PushButton",
                    X = 40, Y = 165, Width = 290, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Dialog.SetupType.Complete)",
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
                    NextControl = "TypicalButton"
                }
            ],
            Events =
            [
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
                    ControlName = "TypicalButton",
                    Event = "NewDialog",
                    Argument = "ProgressDlg",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "CustomButton",
                    Event = "NewDialog",
                    Argument = "CustomizeDlg",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "CompleteButton",
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

    private static MsiDialogModel BuildCustomizeDlg()
    {
        var dlg = "CustomizeDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "Tree",
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
                    Text = "{\\DlgFontBold8}!(loc.Dialog.Customize.Title)"
                },
                new MsiControlModel
                {
                    Name = "Tree",
                    Type = "SelectionTree",
                    X = 25, Y = 55, Width = 175, Height = 130,
                    Attributes = 7,
                    Property = "_BrowseProperty",
                    NextControl = "DiskCost"
                },
                new MsiControlModel
                {
                    Name = "ItemDescription",
                    Type = "Text",
                    X = 210, Y = 55, Width = 140, Height = 50,
                    Attributes = 3
                },
                new MsiControlModel
                {
                    Name = "DiskCost",
                    Type = "VolumeCostList",
                    X = 25, Y = 195, Width = 320, Height = 30,
                    Attributes = 393223,
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
                    Text = "!(loc.Button.Back)",
                    NextControl = "Next"
                },
                new MsiControlModel
                {
                    Name = "Next",
                    Type = "PushButton",
                    X = 236, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Button.Next)",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Button.Cancel)",
                    NextControl = "Tree"
                }
            ],
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Back",
                    Event = "NewDialog",
                    Argument = "SetupTypeDlg",
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
                    Text = "{\\DlgFontBold8}!(loc.Dialog.InstallDir.Title)"
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
                    Text = "!(loc.Dialog.InstallDir.Change)",
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
                    Text = "!(loc.Button.Back)",
                    NextControl = "Next"
                },
                new MsiControlModel
                {
                    Name = "Next",
                    Type = "PushButton",
                    X = 236, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Button.Next)",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Button.Cancel)",
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
                    Argument = "SetupTypeDlg",
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
                    Text = "{\\DlgFontBold8}!(loc.Dialog.Progress.Title)"
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
                    Text = "!(loc.Button.Cancel)"
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
                    Text = "{\\DlgFontBold8}!(loc.Dialog.Complete.Title)"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = "Text",
                    X = 25, Y = 23, Width = 280, Height = 20,
                    Attributes = 196611,
                    Text = "!(loc.Dialog.Complete.Description)"
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
                    Text = "!(loc.Button.Finish)"
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