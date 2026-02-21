using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class MinimalDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            BuildWelcomeDlg(package),
            BuildProgressDlg(package),
            BuildExitDlg(package)
        ];
    }

    private static MsiDialogModel BuildWelcomeDlg(PackageModel package)
    {
        var dlg = "WelcomeDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = $"[ProductName] Setup",
            FirstControl = "Install",
            DefaultControl = "Install",
            CancelControl = "Cancel",
            Controls =
            [
                new MsiControlModel
                {
                    Name = "Title",
                    Type = "Text",
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = 196611, // Visible | Enabled | Transparent | NoPrefix
                    Text = "{\\DlgFontBold8}!(loc.Dialog.Welcome.Title)"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = "Text",
                    X = 25, Y = 23, Width = 280, Height = 20,
                    Attributes = 196611,
                    Text = "!(loc.Dialog.Welcome.Description)"
                },
                new MsiControlModel
                {
                    Name = "BottomLine",
                    Type = "Line",
                    X = 0, Y = 234, Width = 370, Height = 0,
                    Attributes = 1 // Visible
                },
                new MsiControlModel
                {
                    Name = "Install",
                    Type = "PushButton",
                    X = 236, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Button.Install)",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = "PushButton",
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Attributes = 3,
                    Text = "!(loc.Button.Cancel)",
                    NextControl = "Install"
                }
            ],
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Install",
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

    private static MsiDialogModel BuildProgressDlg(PackageModel package)
    {
        var dlg = "ProgressDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = $"[ProductName] Setup",
            Attributes = 5, // Visible | Minimize (not Modal — modeless during install)
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
                    Name = "StatusLabel",
                    Type = "Text",
                    X = 25, Y = 55, Width = 50, Height = 10,
                    Attributes = 3,
                    Text = "!(loc.Dialog.Progress.Status)"
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
                    Attributes = 65539 // Visible | Enabled | Progress95
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

    private static MsiDialogModel BuildExitDlg(PackageModel package)
    {
        var dlg = "ExitDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = $"[ProductName] Setup",
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
