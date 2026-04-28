using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.DialogEditor;

internal static class DialogTemplateProvider
{
    public static IReadOnlyList<DialogDefinition> GetDialogs(string templateName)
    {
        return templateName switch
        {
            "Minimal" => BuildMinimal(),
            "InstallDir" => BuildInstallDir(),
            "FeatureTree" => BuildFeatureTree(),
            "Mondo" => BuildMondo(),
            "Advanced" => BuildAdvanced(),
            _ => []
        };
    }

    private static List<DialogDefinition> BuildMinimal()
    {
        return
        [
            new DialogDefinition
            {
                Name = "WelcomeDlg",
                Title = "[ProductName] Setup",
                Controls =
                [
                    new DialogControlDefinition { Type = DialogControlType.Text, X = 15, Y = 6, Width = 200, Height = 15, Text = "Welcome" },
                    new DialogControlDefinition { Type = DialogControlType.Text, X = 25, Y = 23, Width = 280, Height = 20, Text = "Click Install to begin." },
                    new DialogControlDefinition { Type = DialogControlType.Line, X = 0, Y = 234, Width = 370, Height = 0 },
                    new DialogControlDefinition { Type = DialogControlType.PushButton, X = 236, Y = 243, Width = 56, Height = 17, Text = "Install" },
                    new DialogControlDefinition { Type = DialogControlType.PushButton, X = 304, Y = 243, Width = 56, Height = 17, Text = "Cancel" }
                ]
            },
            new DialogDefinition
            {
                Name = "ProgressDlg",
                Title = "[ProductName] Setup",
                Controls =
                [
                    new DialogControlDefinition { Type = DialogControlType.Text, X = 15, Y = 6, Width = 200, Height = 15, Text = "Installing..." },
                    new DialogControlDefinition { Type = DialogControlType.ProgressBar, X = 25, Y = 70, Width = 320, Height = 10 },
                    new DialogControlDefinition { Type = DialogControlType.Line, X = 0, Y = 234, Width = 370, Height = 0 },
                    new DialogControlDefinition { Type = DialogControlType.PushButton, X = 304, Y = 243, Width = 56, Height = 17, Text = "Cancel" }
                ]
            },
            new DialogDefinition
            {
                Name = "ExitDlg",
                Title = "[ProductName] Setup",
                Controls =
                [
                    new DialogControlDefinition { Type = DialogControlType.Text, X = 15, Y = 6, Width = 200, Height = 15, Text = "Complete" },
                    new DialogControlDefinition { Type = DialogControlType.Text, X = 25, Y = 23, Width = 280, Height = 20, Text = "Installation is complete." },
                    new DialogControlDefinition { Type = DialogControlType.Line, X = 0, Y = 234, Width = 370, Height = 0 },
                    new DialogControlDefinition { Type = DialogControlType.PushButton, X = 304, Y = 243, Width = 56, Height = 17, Text = "Finish" }
                ]
            }
        ];
    }

    private static List<DialogDefinition> BuildInstallDir()
    {
        var dialogs = BuildMinimal();
        dialogs.Insert(1, new DialogDefinition
        {
            Name = "InstallDirDlg",
            Title = "[ProductName] Setup",
            Controls =
            [
                new DialogControlDefinition { Type = DialogControlType.Text, X = 15, Y = 6, Width = 200, Height = 15, Text = "Installation Folder" },
                new DialogControlDefinition { Type = DialogControlType.Text, X = 25, Y = 23, Width = 280, Height = 15, Text = "Choose the folder to install to." },
                new DialogControlDefinition { Type = DialogControlType.PathEdit, X = 20, Y = 80, Width = 260, Height = 18, Property = "INSTALLDIR" },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 284, Y = 80, Width = 56, Height = 17, Text = "Change..." },
                new DialogControlDefinition { Type = DialogControlType.Line, X = 0, Y = 234, Width = 370, Height = 0 },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 180, Y = 243, Width = 56, Height = 17, Text = "Back" },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 236, Y = 243, Width = 56, Height = 17, Text = "Next" },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 304, Y = 243, Width = 56, Height = 17, Text = "Cancel" }
            ]
        });
        return dialogs;
    }

    private static List<DialogDefinition> BuildFeatureTree()
    {
        var dialogs = BuildInstallDir();
        dialogs.Insert(2, new DialogDefinition
        {
            Name = "FeaturesDlg",
            Title = "[ProductName] Setup",
            Controls =
            [
                new DialogControlDefinition { Type = DialogControlType.Text, X = 15, Y = 6, Width = 200, Height = 15, Text = "Custom Setup" },
                new DialogControlDefinition { Type = DialogControlType.Text, X = 25, Y = 23, Width = 280, Height = 15, Text = "Select features to install." },
                new DialogControlDefinition { Type = DialogControlType.ListBox, X = 20, Y = 60, Width = 330, Height = 120, Property = "FeatureSelection" },
                new DialogControlDefinition { Type = DialogControlType.VolumeCostList, X = 20, Y = 185, Width = 330, Height = 40 },
                new DialogControlDefinition { Type = DialogControlType.Line, X = 0, Y = 234, Width = 370, Height = 0 },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 180, Y = 243, Width = 56, Height = 17, Text = "Back" },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 236, Y = 243, Width = 56, Height = 17, Text = "Next" },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 304, Y = 243, Width = 56, Height = 17, Text = "Cancel" }
            ]
        });
        return dialogs;
    }

    private static List<DialogDefinition> BuildMondo()
    {
        var dialogs = BuildFeatureTree();
        dialogs.Insert(1, new DialogDefinition
        {
            Name = "LicenseAgreementDlg",
            Title = "[ProductName] Setup",
            Controls =
            [
                new DialogControlDefinition { Type = DialogControlType.Text, X = 15, Y = 6, Width = 200, Height = 15, Text = "License Agreement" },
                new DialogControlDefinition { Type = DialogControlType.Text, X = 25, Y = 23, Width = 280, Height = 15, Text = "Please read the license agreement." },
                new DialogControlDefinition { Type = DialogControlType.TextEdit, X = 20, Y = 60, Width = 330, Height = 140, Property = "LicenseText" },
                new DialogControlDefinition { Type = DialogControlType.CheckBox, X = 20, Y = 207, Width = 330, Height = 18, Text = "I accept the terms", Property = "LicenseAccepted" },
                new DialogControlDefinition { Type = DialogControlType.Line, X = 0, Y = 234, Width = 370, Height = 0 },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 180, Y = 243, Width = 56, Height = 17, Text = "Back" },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 236, Y = 243, Width = 56, Height = 17, Text = "Next" },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 304, Y = 243, Width = 56, Height = 17, Text = "Cancel" }
            ]
        });
        return dialogs;
    }

    private static List<DialogDefinition> BuildAdvanced()
    {
        var dialogs = BuildMondo();
        dialogs.Insert(dialogs.Count - 2, new DialogDefinition
        {
            Name = "SetupTypeDlg",
            Title = "[ProductName] Setup",
            Controls =
            [
                new DialogControlDefinition { Type = DialogControlType.Text, X = 15, Y = 6, Width = 200, Height = 15, Text = "Setup Type" },
                new DialogControlDefinition { Type = DialogControlType.Text, X = 25, Y = 23, Width = 280, Height = 15, Text = "Choose a setup type." },
                new DialogControlDefinition { Type = DialogControlType.RadioButtonGroup, X = 20, Y = 60, Width = 330, Height = 120, Property = "SetupType" },
                new DialogControlDefinition { Type = DialogControlType.Line, X = 0, Y = 234, Width = 370, Height = 0 },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 180, Y = 243, Width = 56, Height = 17, Text = "Back" },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 236, Y = 243, Width = 56, Height = 17, Text = "Next" },
                new DialogControlDefinition { Type = DialogControlType.PushButton, X = 304, Y = 243, Width = 56, Height = 17, Text = "Cancel" }
            ]
        });
        return dialogs;
    }
}
