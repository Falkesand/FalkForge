using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock InstallDir dialog.
/// </summary>
/// <remarks>
/// Mirrors the legacy <c>SharedDialogBuilders.BuildInstallDirDlg</c> control set: bold title,
/// description block, folder label, INSTALLDIR-bound PathEdit, Change folder button, BottomLine,
/// and Back/Next/Cancel in the right-packed ButtonRow. ButtonRow gap is the layout default 8 DLU,
/// so non-rightmost button positions are structurally equivalent rather than byte-identical.
/// </remarks>
internal static class InstallDirDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "InstallDirDlg";

    /// <summary>Builds the declarative content for the InstallDir dialog.</summary>
    public static DialogContent Build()
    {
        return new DialogContent
        {
            Name = DialogName,
            Kind = "InstallDir",
            FirstControl = "Folder",
            DefaultControl = "Next",
            CancelControl = "Cancel",
            TitleLocKey = "[ProductName] Setup",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "TitleRow",
                    Controls = ImmutableArray.Create(
                        new PlacedControl
                        {
                            Name = "Title",
                            Type = "Text",
                            TextOrLocKey = "{\\DlgFontBold8}!(loc.Dialog.InstallDir.Title)",
                            OverrideWidth = 200,
                            OverrideHeight = 15,
                        }),
                },
                new RegionPlacement
                {
                    RegionName = "ContentArea",
                    Controls = ImmutableArray.Create(
                        new PlacedControl
                        {
                            Name = "Description",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.InstallDir.Description)",
                            OverrideX = 25,
                            OverrideY = 23,
                            OverrideWidth = 280,
                            OverrideHeight = 15,
                        },
                        new PlacedControl
                        {
                            Name = "FolderLabel",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.InstallDir.Label)",
                            OverrideX = 20,
                            OverrideY = 60,
                            OverrideWidth = 290,
                            OverrideHeight = 15,
                        },
                        new PlacedControl
                        {
                            Name = "Folder",
                            Type = "PathEdit",
                            Property = "INSTALLDIR",
                            OverrideX = 20,
                            OverrideY = 80,
                            OverrideWidth = 260,
                            OverrideHeight = 18,
                        },
                        new PlacedControl
                        {
                            Name = "ChangeFolder",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Dialog.InstallDir.Change)",
                            OverrideX = 284,
                            OverrideY = 80,
                            OverrideWidth = 56,
                            OverrideHeight = 17,
                        }),
                },
                new RegionPlacement
                {
                    RegionName = "BottomLine",
                    Controls = ImmutableArray.Create(
                        new PlacedControl
                        {
                            Name = "BottomLine",
                            Type = "Line",
                        }),
                },
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(
                        new PlacedControl
                        {
                            Name = "Cancel",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Button.Cancel)",
                        },
                        new PlacedControl
                        {
                            Name = "Next",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Button.Next)",
                        },
                        new PlacedControl
                        {
                            Name = "Back",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Button.Back)",
                        }),
                }),
        };
    }
}
