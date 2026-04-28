# Dialog Template Deduplication Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extract shared dialog builders from 5 MSI dialog templates into a single `SharedDialogBuilders` static class, eliminating ~1,700 lines of duplication while preserving identical MSI output.

**Architecture:** Each shared dialog builder is a static method that accepts only the parameters that vary across templates (navigation targets, optional controls). Each template becomes a thin orchestrator calling shared builders. Minimal's WelcomeDlg stays custom (structurally different — Install button vs Next/Back).

**Tech Stack:** C#, existing `MsiDialogModel`/`MsiControlModel`/`MsiControlEventModel`/`MsiControlConditionModel` types.

---

### Task 1: Create SharedDialogBuilders with BuildExitDlg

**Files:**
- Create: `src/FalkForge.Compiler.Msi/UI/Templates/SharedDialogBuilders.cs`

**Step 1: Create the file with BuildExitDlg (identical across all 5 templates)**

```csharp
namespace FalkForge.Compiler.Msi.UI.Templates;

internal static class SharedDialogBuilders
{
    internal static MsiDialogModel BuildExitDlg()
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
```

**Step 2: Build to verify**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Expected: Build succeeded. 0 warnings.

**Step 3: Commit**

```
refactor(msi): add SharedDialogBuilders with BuildExitDlg
```

---

### Task 2: Add BuildProgressDlg to SharedDialogBuilders

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/SharedDialogBuilders.cs`

**Step 1: Add BuildProgressDlg**

Two variants exist:
- Minimal/InstallDir: includes StatusLabel control
- FeatureTree/Mondo/Advanced: no StatusLabel

```csharp
internal static MsiDialogModel BuildProgressDlg(bool includeStatusLabel)
{
    var dlg = "ProgressDlg";
    var controls = new List<MsiControlModel>
    {
        new()
        {
            Name = "Title",
            Type = "Text",
            X = 15, Y = 6, Width = 200, Height = 15,
            Attributes = 196611,
            Text = "{\\DlgFontBold8}!(loc.Dialog.Progress.Title)"
        }
    };

    if (includeStatusLabel)
    {
        controls.Add(new MsiControlModel
        {
            Name = "StatusLabel",
            Type = "Text",
            X = 25, Y = 55, Width = 50, Height = 10,
            Attributes = 3,
            Text = "!(loc.Dialog.Progress.Status)"
        });
    }

    controls.AddRange([
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
    ]);

    return new MsiDialogModel
    {
        Name = dlg,
        Title = "[ProductName] Setup",
        Attributes = 5,
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
                Event = "SpawnDialog",
                Argument = "CancelDlg",
                Ordering = 1
            }
        ]
    };
}
```

**Step 2: Build to verify**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Expected: Build succeeded. 0 warnings.

**Step 3: Commit**

```
refactor(msi): add BuildProgressDlg to SharedDialogBuilders
```

---

### Task 3: Add BuildWelcomeDlg to SharedDialogBuilders

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/SharedDialogBuilders.cs`

**Step 1: Add BuildWelcomeDlg**

Used by InstallDir, FeatureTree, Mondo, Advanced (NOT Minimal — Minimal has Install button layout).
Only the Next target varies.

```csharp
internal static MsiDialogModel BuildWelcomeDlg(string nextDialog)
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
                Argument = nextDialog,
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
```

**Step 2: Build to verify**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Expected: Build succeeded. 0 warnings.

**Step 3: Commit**

```
refactor(msi): add BuildWelcomeDlg to SharedDialogBuilders
```

---

### Task 4: Add BuildLicenseAgreementDlg to SharedDialogBuilders

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/SharedDialogBuilders.cs`

**Step 1: Add BuildLicenseAgreementDlg**

Variations:
- `backDialog`: WelcomeDlg (InstallDir/FeatureTree/Mondo), InstallScopeDlg (Advanced)
- `nextDialog`: InstallDirDlg (InstallDir), CustomizeDlg (FeatureTree), SetupTypeDlg (Mondo/Advanced)
- `includeDescription`: true (InstallDir only), false (FeatureTree/Mondo/Advanced)

```csharp
internal static MsiDialogModel BuildLicenseAgreementDlg(
    string backDialog, string nextDialog, bool includeDescription = false)
{
    var dlg = "LicenseAgreementDlg";
    var controls = new List<MsiControlModel>
    {
        new()
        {
            Name = "Title",
            Type = "Text",
            X = 15, Y = 6, Width = 200, Height = 15,
            Attributes = 196611,
            Text = "{\\DlgFontBold8}!(loc.Dialog.License.Title)"
        }
    };

    if (includeDescription)
    {
        controls.Add(new MsiControlModel
        {
            Name = "Description",
            Type = "Text",
            X = 25, Y = 23, Width = 280, Height = 15,
            Attributes = 196611,
            Text = "!(loc.Dialog.License.Description)"
        });
    }

    controls.AddRange([
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
                Event = "NewDialog",
                Argument = backDialog,
                Ordering = 1
            },
            new MsiControlEventModel
            {
                DialogName = dlg,
                ControlName = "Next",
                Event = "NewDialog",
                Argument = nextDialog,
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
```

**Step 2: Build to verify**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Expected: Build succeeded. 0 warnings.

**Step 3: Commit**

```
refactor(msi): add BuildLicenseAgreementDlg to SharedDialogBuilders
```

---

### Task 5: Add BuildInstallDirDlg and BuildCustomizeDlg to SharedDialogBuilders

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/SharedDialogBuilders.cs`

**Step 1: Add BuildInstallDirDlg**

Used by InstallDir, Mondo, Advanced. Only `backDialog` varies. Next is always ProgressDlg.

```csharp
internal static MsiDialogModel BuildInstallDirDlg(string backDialog)
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
                Name = "Description",
                Type = "Text",
                X = 25, Y = 23, Width = 280, Height = 15,
                Attributes = 196611,
                Text = "!(loc.Dialog.InstallDir.Description)"
            },
            new MsiControlModel
            {
                Name = "FolderLabel",
                Type = "Text",
                X = 20, Y = 60, Width = 290, Height = 15,
                Attributes = 3,
                Text = "!(loc.Dialog.InstallDir.Label)"
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
                Argument = backDialog,
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
```

**Step 2: Add BuildCustomizeDlg**

Used by FeatureTree, Mondo, Advanced. Only `backDialog` varies. Next is always ProgressDlg.

```csharp
internal static MsiDialogModel BuildCustomizeDlg(string backDialog)
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
                Name = "Description",
                Type = "Text",
                X = 25, Y = 23, Width = 280, Height = 15,
                Attributes = 196611,
                Text = "!(loc.Dialog.Customize.Description)"
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
                Argument = backDialog,
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
```

**Step 3: Build to verify**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Expected: Build succeeded. 0 warnings.

**Step 4: Commit**

```
refactor(msi): add BuildInstallDirDlg and BuildCustomizeDlg to SharedDialogBuilders
```

---

### Task 6: Add BuildSetupTypeDlg to SharedDialogBuilders

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/SharedDialogBuilders.cs`

**Step 1: Add BuildSetupTypeDlg**

Used by Mondo and Advanced. Differences:
- Mondo includes Description text + TypicalDesc/CustomDesc/CompleteDesc description controls
- Advanced omits all description controls

```csharp
internal static MsiDialogModel BuildSetupTypeDlg(bool includeDescriptions)
{
    var dlg = "SetupTypeDlg";
    var controls = new List<MsiControlModel>
    {
        new()
        {
            Name = "Title",
            Type = "Text",
            X = 15, Y = 6, Width = 200, Height = 15,
            Attributes = 196611,
            Text = "{\\DlgFontBold8}!(loc.Dialog.SetupType.Title)"
        }
    };

    if (includeDescriptions)
    {
        controls.Add(new MsiControlModel
        {
            Name = "Description",
            Type = "Text",
            X = 25, Y = 23, Width = 280, Height = 15,
            Attributes = 196611,
            Text = "!(loc.Dialog.SetupType.Description)"
        });
    }

    controls.Add(new MsiControlModel
    {
        Name = "TypicalButton",
        Type = "PushButton",
        X = 40, Y = 65, Width = 290, Height = 17,
        Attributes = 3,
        Text = "!(loc.Dialog.SetupType.Typical)",
        NextControl = "CustomButton"
    });

    if (includeDescriptions)
    {
        controls.Add(new MsiControlModel
        {
            Name = "TypicalDesc",
            Type = "Text",
            X = 60, Y = 85, Width = 270, Height = 20,
            Attributes = 196611,
            Text = "!(loc.Dialog.SetupType.TypicalDesc)"
        });
    }

    controls.Add(new MsiControlModel
    {
        Name = "CustomButton",
        Type = "PushButton",
        X = 40, Y = 115, Width = 290, Height = 17,
        Attributes = 3,
        Text = "!(loc.Dialog.SetupType.Custom)",
        NextControl = "CompleteButton"
    });

    if (includeDescriptions)
    {
        controls.Add(new MsiControlModel
        {
            Name = "CustomDesc",
            Type = "Text",
            X = 60, Y = 135, Width = 270, Height = 20,
            Attributes = 196611,
            Text = "!(loc.Dialog.SetupType.CustomDesc)"
        });
    }

    controls.AddRange([
        new MsiControlModel
        {
            Name = "CompleteButton",
            Type = "PushButton",
            X = 40, Y = 165, Width = 290, Height = 17,
            Attributes = 3,
            Text = "!(loc.Dialog.SetupType.Complete)",
            NextControl = "Back"
        }
    ]);

    if (includeDescriptions)
    {
        controls.Add(new MsiControlModel
        {
            Name = "CompleteDesc",
            Type = "Text",
            X = 60, Y = 185, Width = 270, Height = 20,
            Attributes = 196611,
            Text = "!(loc.Dialog.SetupType.CompleteDesc)"
        });
    }

    controls.AddRange([
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
```

**Step 2: Build to verify**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Expected: Build succeeded. 0 warnings.

**Step 3: Commit**

```
refactor(msi): add BuildSetupTypeDlg to SharedDialogBuilders
```

---

### Task 7: Refactor MinimalDialogTemplate to use SharedDialogBuilders

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/MinimalDialogTemplate.cs`

**Step 1: Replace BuildExitDlg and BuildProgressDlg with shared calls**

Minimal's WelcomeDlg stays private (structurally unique — Install button, no Next/Back).

```csharp
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class MinimalDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            BuildWelcomeDlg(),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: true),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }

    private static MsiDialogModel BuildWelcomeDlg()
    {
        var dlg = "WelcomeDlg";
        return new MsiDialogModel
        {
            Name = dlg,
            Title = "[ProductName] Setup",
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
                    Attributes = 196611,
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
                    Attributes = 1
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
}
```

**Step 2: Build and run tests**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj --filter "FullyQualifiedName~DemoEndToEnd" --no-build -v minimal`
Expected: All 72 tests pass.

**Step 3: Commit**

```
refactor(msi): MinimalDialogTemplate uses SharedDialogBuilders
```

---

### Task 8: Refactor InstallDirDialogTemplate to use SharedDialogBuilders

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/InstallDirDialogTemplate.cs`

**Step 1: Replace all private builders with shared calls**

```csharp
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
```

**Step 2: Build and run tests**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj --filter "FullyQualifiedName~DemoEndToEnd" --no-build -v minimal`
Expected: All 72 tests pass.

**Step 3: Commit**

```
refactor(msi): InstallDirDialogTemplate uses SharedDialogBuilders
```

---

### Task 9: Refactor FeatureTreeDialogTemplate to use SharedDialogBuilders

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/FeatureTreeDialogTemplate.cs`

**Step 1: Replace all private builders with shared calls**

```csharp
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class FeatureTreeDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            SharedDialogBuilders.BuildWelcomeDlg(nextDialog: "LicenseAgreementDlg"),
            SharedDialogBuilders.BuildLicenseAgreementDlg(
                backDialog: "WelcomeDlg",
                nextDialog: "CustomizeDlg"),
            SharedDialogBuilders.BuildCustomizeDlg(backDialog: "LicenseAgreementDlg"),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: false),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }
}
```

**Step 2: Build and run tests**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj --filter "FullyQualifiedName~DemoEndToEnd" --no-build -v minimal`
Expected: All 72 tests pass.

**Step 3: Commit**

```
refactor(msi): FeatureTreeDialogTemplate uses SharedDialogBuilders
```

---

### Task 10: Refactor MondoDialogTemplate to use SharedDialogBuilders

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/MondoDialogTemplate.cs`

**Step 1: Replace all private builders with shared calls**

```csharp
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

internal sealed class MondoDialogTemplate : IDialogTemplate
{
    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
    {
        return
        [
            SharedDialogBuilders.BuildWelcomeDlg(nextDialog: "LicenseAgreementDlg"),
            SharedDialogBuilders.BuildLicenseAgreementDlg(
                backDialog: "WelcomeDlg",
                nextDialog: "SetupTypeDlg"),
            SharedDialogBuilders.BuildSetupTypeDlg(includeDescriptions: true),
            SharedDialogBuilders.BuildCustomizeDlg(backDialog: "SetupTypeDlg"),
            SharedDialogBuilders.BuildInstallDirDlg(backDialog: "SetupTypeDlg"),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: false),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }
}
```

**Step 2: Build and run tests**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj --filter "FullyQualifiedName~DemoEndToEnd" --no-build -v minimal`
Expected: All 72 tests pass.

**Step 3: Commit**

```
refactor(msi): MondoDialogTemplate uses SharedDialogBuilders
```

---

### Task 11: Refactor AdvancedDialogTemplate to use SharedDialogBuilders

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/AdvancedDialogTemplate.cs`

**Step 1: Replace shared builders, keep BuildInstallScopeDlg private**

InstallScopeDlg is unique to Advanced — it stays as a private method.

```csharp
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
            SharedDialogBuilders.BuildCustomizeDlg(backDialog: "SetupTypeDlg"),
            SharedDialogBuilders.BuildInstallDirDlg(backDialog: "SetupTypeDlg"),
            SharedDialogBuilders.BuildProgressDlg(includeStatusLabel: false),
            SharedDialogBuilders.BuildExitDlg()
        ];
    }

    // InstallScopeDlg is unique to Advanced — per-machine vs per-user selection
    private static MsiDialogModel BuildInstallScopeDlg()
    {
        // Keep the existing BuildInstallScopeDlg implementation as-is
        // (lines 98-238 of the current file)
    }
}
```

The subagent should copy the existing `BuildInstallScopeDlg` method body verbatim from lines 98-238 of the current `AdvancedDialogTemplate.cs`. Do NOT modify it.

**Step 2: Build and run tests**

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj`
Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/FalkForge.Integration.Tests.csproj --filter "FullyQualifiedName~DemoEndToEnd" --no-build -v minimal`
Expected: All 72 tests pass.

**Step 3: Commit**

```
refactor(msi): AdvancedDialogTemplate uses SharedDialogBuilders
```

---

### Task 12: Final verification and full test suite

**Step 1: Build full solution**

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
Expected: Build succeeded. 0 warnings.

**Step 2: Run full test suite**

Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx -v minimal`
Expected: All ~3,918 tests pass.

**Step 3: Run quickdup to verify duplication reduction**

Run: `quickdup -path D:/Git/FalkInstaller/src/FalkForge.Compiler.Msi/UI/Templates -ext .cs -top 5`
Expected: Significant reduction in duplicate pattern count and score.

**Step 4: Commit if any cleanup needed, otherwise done**

```
refactor(msi): complete dialog template deduplication
```
