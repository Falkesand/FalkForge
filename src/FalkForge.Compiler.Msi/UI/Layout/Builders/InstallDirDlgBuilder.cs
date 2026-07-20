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

    /// <summary>Builds the declarative content for the InstallDir dialog with default flow context.</summary>
    public static DialogContent Build() => Build(new DialogFlowContext());

    /// <summary>Builds the declarative content for the InstallDir dialog with explicit flow targets.</summary>
    /// <param name="flow">Navigation targets for ChangeFolder/Back/Next/Cancel events.</param>
    public static DialogContent Build(DialogFlowContext flow)
    {
        System.ArgumentNullException.ThrowIfNull(flow);

        // Mirrors legacy SharedDialogBuilders.BuildInstallDirDlg event set:
        //   ChangeFolder #1: SetProperty[_BrowseProperty] = [INSTALLDIR]
        //   ChangeFolder #2: SpawnDialog BrowseDlg
        //   ChangeFolder #3: SetProperty[INSTALLDIR] = [_BrowseProperty]
        //   Back: NewDialog flow.BackDialog
        //   Next: EndDialog Return
        //   Cancel: SpawnDialog flow.CancelDialog
        var events = ImmutableArray.Create(
            new DialogControlEvent
            {
                Control = "ChangeFolder",
                Event = "[_BrowseProperty]",
                Argument = "[INSTALLDIR]",
                Order = 1,
            },
            new DialogControlEvent
            {
                Control = "ChangeFolder",
                Event = "SpawnDialog",
                Argument = "BrowseDlg",
                Order = 2,
            },
            new DialogControlEvent
            {
                Control = "ChangeFolder",
                Event = "[INSTALLDIR]",
                Argument = "[_BrowseProperty]",
                Order = 3,
            },
            DialogFooter.BackEvent(flow),
            new DialogControlEvent
            {
                Control = "Next",
                Event = "EndDialog",
                Argument = "Return",
            },
            DialogFooter.CancelEvent(flow));

        return new DialogContent
        {
            Name = DialogName,
            Kind = "InstallDir",
            FirstControl = "Folder",
            DefaultControl = "Next",
            CancelControl = "Cancel",
            TitleLocKey = "[ProductName] Setup",
            Events = events,
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
                DialogFooter.BottomLine(),
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(
                        DialogFooter.CancelButton(),
                        DialogFooter.NextButton(),
                        DialogFooter.BackButton()),
                }),
        };
    }
}
