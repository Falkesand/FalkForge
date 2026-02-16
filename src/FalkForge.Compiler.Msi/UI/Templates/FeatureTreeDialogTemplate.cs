using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class FeatureTreeDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            BuildWelcomeDlg(),
            BuildLicenseAgreementDlg(),
            BuildCustomizeDlg(),
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
                    Argument = "CustomizeDlg",
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
                    Text = "{\\DlgFontBold8}Custom Setup"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = "Text",
                    X = 25, Y = 23, Width = 280, Height = 15,
                    Attributes = 196611,
                    Text = "Select the features you want installed."
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
                    Name = "ItemSize",
                    Type = "Text",
                    X = 210, Y = 110, Width = 140, Height = 15,
                    Attributes = 3
                },
                new MsiControlModel
                {
                    Name = "DiskCost",
                    Type = "VolumeCostList",
                    X = 25, Y = 195, Width = 320, Height = 30,
                    Attributes = 393223, // Visible | Enabled | Sunken | CDROM | Fixed | Remote | Floppy
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
