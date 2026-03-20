namespace FalkForge.Compiler.Msi.UI.Templates;

internal static class SharedDialogBuilders
{
    internal static MsiDialogModel BuildExitDlg()
    {
        var dlg = DialogNames.Exit;
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
                    Type = MsiControlType.Text,
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                    Text = "{\\DlgFontBold8}!(loc.Dialog.Complete.Title)"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = MsiControlType.Text,
                    X = 25, Y = 23, Width = 280, Height = 20,
                    Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                    Text = "!(loc.Dialog.Complete.Description)"
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
                    Name = "Finish",
                    Type = MsiControlType.PushButton,
                    X = 304, Y = 243, Width = 56, Height = 17,
                    Text = "!(loc.Button.Finish)"
                }
            ],
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Finish",
                    Event = MsiControlEvent.EndDialog,
                    Argument = "Return",
                    Ordering = 1
                }
            ]
        };
    }

    internal static MsiDialogModel BuildProgressDlg(bool includeStatusLabel)
    {
        var dlg = DialogNames.Progress;

        var controls = new List<MsiControlModel>
        {
            new()
            {
                Name = "Title",
                Type = MsiControlType.Text,
                X = 15, Y = 6, Width = 200, Height = 15,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "{\\DlgFontBold8}!(loc.Dialog.Progress.Title)"
            }
        };

        if (includeStatusLabel)
        {
            controls.Add(new MsiControlModel
            {
                Name = "StatusLabel",
                Type = MsiControlType.Text,
                X = 25, Y = 55, Width = 50, Height = 10,
                Text = "!(loc.Dialog.Progress.Status)"
            });
        }

        controls.AddRange(
        [
            new MsiControlModel
            {
                Name = "ActionText",
                Type = MsiControlType.Text,
                X = 75, Y = 55, Width = 270, Height = 10
            },
            new MsiControlModel
            {
                Name = "ProgressBar",
                Type = MsiControlType.ProgressBar,
                X = 25, Y = 70, Width = 320, Height = 10,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Progress95
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
                Name = "Cancel",
                Type = MsiControlType.PushButton,
                X = 304, Y = 243, Width = 56, Height = 17,
                Text = "!(loc.Button.Cancel)"
            }
        ]);

        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            Attributes = MsiDialogAttributes.Visible | MsiDialogAttributes.Minimize,
            FirstControl = "Cancel",
            DefaultControl = "Cancel",
            CancelControl = "Cancel",
            Controls = controls,
            Events =
            [
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

    internal static MsiDialogModel BuildWelcomeDlg(string nextDialog)
    {
        var dlg = DialogNames.Welcome;
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
                    Type = MsiControlType.Text,
                    X = 15, Y = 6, Width = 200, Height = 15,
                    Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                    Text = "{\\DlgFontBold8}!(loc.Dialog.Welcome.Title)"
                },
                new MsiControlModel
                {
                    Name = "Description",
                    Type = MsiControlType.Text,
                    X = 25, Y = 23, Width = 280, Height = 40,
                    Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                    Text = "!(loc.Dialog.Welcome.DescriptionFull)"
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
                    Name = "Next",
                    Type = MsiControlType.PushButton,
                    X = 236, Y = 243, Width = 56, Height = 17,
                    Text = "!(loc.Button.Next)",
                    NextControl = "Cancel"
                },
                new MsiControlModel
                {
                    Name = "Cancel",
                    Type = MsiControlType.PushButton,
                    X = 304, Y = 243, Width = 56, Height = 17,
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
                    Event = MsiControlEvent.NewDialog,
                    Argument = nextDialog,
                    Ordering = 1
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

    internal static MsiDialogModel BuildLicenseAgreementDlg(
        string backDialog,
        string nextDialog,
        bool includeDescription = false)
    {
        var dlg = DialogNames.LicenseAgreement;

        var controls = new List<MsiControlModel>
        {
            new()
            {
                Name = "Title",
                Type = MsiControlType.Text,
                X = 15, Y = 6, Width = 200, Height = 15,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "{\\DlgFontBold8}!(loc.Dialog.License.Title)"
            }
        };

        if (includeDescription)
        {
            controls.Add(new MsiControlModel
            {
                Name = "Description",
                Type = MsiControlType.Text,
                X = 25, Y = 23, Width = 280, Height = 15,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "!(loc.Dialog.License.Description)"
            });
        }

        controls.AddRange(
        [
            new MsiControlModel
            {
                Name = "LicenseText",
                Type = MsiControlType.ScrollableText,
                X = 20, Y = 60, Width = 330, Height = 140,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Sunken,
                Property = "LicenseText",
                NextControl = "LicenseAccepted"
            },
            new MsiControlModel
            {
                Name = "LicenseAccepted",
                Type = MsiControlType.CheckBox,
                X = 20, Y = 207, Width = 330, Height = 18,
                Property = "LicenseAccepted",
                Text = "!(loc.Dialog.License.Accept)",
                NextControl = "Back"
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
                NextControl = "Next"
            },
            new MsiControlModel
            {
                Name = "Next",
                Type = MsiControlType.PushButton,
                X = 236, Y = 243, Width = 56, Height = 17,
                Text = "!(loc.Button.Next)",
                NextControl = "Cancel"
            },
            new MsiControlModel
            {
                Name = "Cancel",
                Type = MsiControlType.PushButton,
                X = 304, Y = 243, Width = 56, Height = 17,
                Text = "!(loc.Button.Cancel)",
                NextControl = "LicenseText"
            }
        ]);

        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "LicenseText",
            DefaultControl = "Next",
            CancelControl = "Cancel",
            Controls = controls,
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Back",
                    Event = MsiControlEvent.NewDialog,
                    Argument = backDialog,
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Next",
                    Event = MsiControlEvent.NewDialog,
                    Argument = nextDialog,
                    Condition = "LicenseAccepted = \"1\"",
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Cancel",
                    Event = MsiControlEvent.SpawnDialog,
                    Argument = DialogNames.Cancel,
                    Ordering = 1
                }
            ],
            Conditions =
            [
                new MsiControlConditionModel
                {
                    DialogName = dlg,
                    ControlName = "Next",
                    Action = MsiConditionAction.Disable,
                    Condition = "NOT LicenseAccepted = \"1\""
                },
                new MsiControlConditionModel
                {
                    DialogName = dlg,
                    ControlName = "Next",
                    Action = MsiConditionAction.Enable,
                    Condition = "LicenseAccepted = \"1\""
                }
            ]
        };
    }

    internal static MsiDialogModel BuildInstallDirDlg(string backDialog, bool includeDescription = true)
    {
        var dlg = DialogNames.InstallDir;

        var controls = new List<MsiControlModel>
        {
            new()
            {
                Name = "Title",
                Type = MsiControlType.Text,
                X = 15, Y = 6, Width = 200, Height = 15,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "{\\DlgFontBold8}!(loc.Dialog.InstallDir.Title)"
            }
        };

        if (includeDescription)
        {
            controls.Add(new MsiControlModel
            {
                Name = "Description",
                Type = MsiControlType.Text,
                X = 25, Y = 23, Width = 280, Height = 15,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "!(loc.Dialog.InstallDir.Description)"
            });
            controls.Add(new MsiControlModel
            {
                Name = "FolderLabel",
                Type = MsiControlType.Text,
                X = 20, Y = 60, Width = 290, Height = 15,
                Text = "!(loc.Dialog.InstallDir.Label)"
            });
        }

        controls.AddRange(
        [
            new MsiControlModel
            {
                Name = "Folder",
                Type = MsiControlType.PathEdit,
                X = 20, Y = 80, Width = 260, Height = 18,
                Property = "WIXUI_INSTALLDIR",
                NextControl = "ChangeFolder"
            },
            new MsiControlModel
            {
                Name = "ChangeFolder",
                Type = MsiControlType.PushButton,
                X = 284, Y = 80, Width = 56, Height = 17,
                Text = "!(loc.Dialog.InstallDir.Change)",
                NextControl = "Back"
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
                NextControl = "Next"
            },
            new MsiControlModel
            {
                Name = "Next",
                Type = MsiControlType.PushButton,
                X = 236, Y = 243, Width = 56, Height = 17,
                Text = "!(loc.Button.Next)",
                NextControl = "Cancel"
            },
            new MsiControlModel
            {
                Name = "Cancel",
                Type = MsiControlType.PushButton,
                X = 304, Y = 243, Width = 56, Height = 17,
                Text = "!(loc.Button.Cancel)",
                NextControl = "Folder"
            }
        ]);

        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "Folder",
            DefaultControl = "Next",
            CancelControl = "Cancel",
            Controls = controls,
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "ChangeFolder",
                    Event = MsiControlEvent.SpawnDialog,
                    Argument = DialogNames.Browse,
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Back",
                    Event = MsiControlEvent.NewDialog,
                    Argument = backDialog,
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Next",
                    Event = MsiControlEvent.NewDialog,
                    Argument = DialogNames.Progress,
                    Ordering = 1
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

    internal static MsiDialogModel BuildCustomizeDlg(string backDialog, bool includeDescription = true)
    {
        var dlg = DialogNames.Customize;

        var controls = new List<MsiControlModel>
        {
            new()
            {
                Name = "Title",
                Type = MsiControlType.Text,
                X = 15, Y = 6, Width = 200, Height = 15,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "{\\DlgFontBold8}!(loc.Dialog.Customize.Title)"
            }
        };

        if (includeDescription)
        {
            controls.Add(new MsiControlModel
            {
                Name = "Description",
                Type = MsiControlType.Text,
                X = 25, Y = 23, Width = 280, Height = 15,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "!(loc.Dialog.Customize.Description)"
            });
        }

        controls.AddRange(
        [
            new MsiControlModel
            {
                Name = "Tree",
                Type = MsiControlType.SelectionTree,
                X = 25, Y = 55, Width = 175, Height = 130,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Sunken,
                Property = "_BrowseProperty",
                NextControl = "DiskCost"
            },
            new MsiControlModel
            {
                Name = "ItemDescription",
                Type = MsiControlType.Text,
                X = 210, Y = 55, Width = 140, Height = 50
            }
        ]);

        if (includeDescription)
        {
            controls.Add(new MsiControlModel
            {
                Name = "ItemSize",
                Type = MsiControlType.Text,
                X = 210, Y = 110, Width = 140, Height = 15
            });
        }

        controls.AddRange(
        [
            new MsiControlModel
            {
                Name = "DiskCost",
                Type = MsiControlType.VolumeCostList,
                X = 25, Y = 195, Width = 320, Height = 30,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Sunken | MsiControlAttributes.RemovableMedia | MsiControlAttributes.FixedMedia | MsiControlAttributes.RemoteMedia,
                NextControl = "Back"
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
                NextControl = "Next"
            },
            new MsiControlModel
            {
                Name = "Next",
                Type = MsiControlType.PushButton,
                X = 236, Y = 243, Width = 56, Height = 17,
                Text = "!(loc.Button.Next)",
                NextControl = "Cancel"
            },
            new MsiControlModel
            {
                Name = "Cancel",
                Type = MsiControlType.PushButton,
                X = 304, Y = 243, Width = 56, Height = 17,
                Text = "!(loc.Button.Cancel)",
                NextControl = "Tree"
            }
        ]);

        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "Tree",
            DefaultControl = "Next",
            CancelControl = "Cancel",
            Controls = controls,
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Back",
                    Event = MsiControlEvent.NewDialog,
                    Argument = backDialog,
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Next",
                    Event = MsiControlEvent.NewDialog,
                    Argument = DialogNames.Progress,
                    Ordering = 1
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

    internal static MsiDialogModel BuildSetupTypeDlg(bool includeDescriptions)
    {
        var dlg = DialogNames.SetupType;

        var controls = new List<MsiControlModel>
        {
            new()
            {
                Name = "Title",
                Type = MsiControlType.Text,
                X = 15, Y = 6, Width = 200, Height = 15,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "{\\DlgFontBold8}!(loc.Dialog.SetupType.Title)"
            }
        };

        if (includeDescriptions)
        {
            controls.Add(new MsiControlModel
            {
                Name = "Description",
                Type = MsiControlType.Text,
                X = 25, Y = 23, Width = 280, Height = 15,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "!(loc.Dialog.SetupType.Description)"
            });
        }

        controls.Add(new MsiControlModel
        {
            Name = "TypicalButton",
            Type = MsiControlType.PushButton,
            X = 40, Y = 65, Width = 290, Height = 17,
            Text = "!(loc.Dialog.SetupType.Typical)",
            NextControl = "CustomButton"
        });

        if (includeDescriptions)
        {
            controls.Add(new MsiControlModel
            {
                Name = "TypicalDesc",
                Type = MsiControlType.Text,
                X = 60, Y = 85, Width = 270, Height = 20,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "!(loc.Dialog.SetupType.TypicalDesc)"
            });
        }

        controls.Add(new MsiControlModel
        {
            Name = "CustomButton",
            Type = MsiControlType.PushButton,
            X = 40, Y = 115, Width = 290, Height = 17,
            Text = "!(loc.Dialog.SetupType.Custom)",
            NextControl = "CompleteButton"
        });

        if (includeDescriptions)
        {
            controls.Add(new MsiControlModel
            {
                Name = "CustomDesc",
                Type = MsiControlType.Text,
                X = 60, Y = 135, Width = 270, Height = 20,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "!(loc.Dialog.SetupType.CustomDesc)"
            });
        }

        controls.Add(new MsiControlModel
        {
            Name = "CompleteButton",
            Type = MsiControlType.PushButton,
            X = 40, Y = 165, Width = 290, Height = 17,
            Text = "!(loc.Dialog.SetupType.Complete)",
            NextControl = "Back"
        });

        if (includeDescriptions)
        {
            controls.Add(new MsiControlModel
            {
                Name = "CompleteDesc",
                Type = MsiControlType.Text,
                X = 60, Y = 185, Width = 270, Height = 20,
                Attributes = MsiControlAttributes.Visible | MsiControlAttributes.Enabled | MsiControlAttributes.Transparent | MsiControlAttributes.NoPrefix,
                Text = "!(loc.Dialog.SetupType.CompleteDesc)"
            });
        }

        controls.AddRange(
        [
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
                NextControl = "TypicalButton"
            }
        ]);

        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
            FirstControl = "TypicalButton",
            DefaultControl = "TypicalButton",
            CancelControl = "Cancel",
            Controls = controls,
            Events =
            [
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "Back",
                    Event = MsiControlEvent.NewDialog,
                    Argument = DialogNames.LicenseAgreement,
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "TypicalButton",
                    Event = MsiControlEvent.NewDialog,
                    Argument = DialogNames.Progress,
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "CustomButton",
                    Event = MsiControlEvent.NewDialog,
                    Argument = DialogNames.Customize,
                    Ordering = 1
                },
                new MsiControlEventModel
                {
                    DialogName = dlg,
                    ControlName = "CompleteButton",
                    Event = MsiControlEvent.NewDialog,
                    Argument = DialogNames.Progress,
                    Ordering = 1
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
