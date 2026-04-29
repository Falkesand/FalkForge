using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock Browse folder dialog.
/// </summary>
/// <remarks>
/// Mirrors the legacy <c>SharedDialogBuilders.BuildBrowseDlg</c> control set: a path label,
/// a PathEdit and DirectoryList both bound to <c>_BrowseProperty</c>, Up / NewFolder navigation
/// buttons, and OK / Cancel in the right-packed ButtonRow.
/// <para>
/// <b>Layout deviation:</b> the legacy modal uses a 240x281 canvas with custom geometry. This
/// builder still targets <see cref="Layouts.Standard370x270"/> because per-template layouts
/// arrive in phase 7+. The output is structurally equivalent to the legacy modal (same controls,
/// same names, same types, same property bindings) rather than byte-identical.
/// </para>
/// </remarks>
internal static class BrowseDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "BrowseDlg";

    /// <summary>Builds the declarative content for the Browse modal. Up and NewFolder use the
    /// MSI directory-list verbs; OK and Cancel both end the dialog with "Return" so the caller
    /// dialog reads the resolved <c>_BrowseProperty</c>. The modal is self-contained.</summary>
    public static DialogContent Build()
    {
        var events = ImmutableArray.Create(
            new DialogControlEvent
            {
                Control = "Up",
                Event = "DirectoryListUp",
                Argument = "0",
            },
            new DialogControlEvent
            {
                Control = "NewFolder",
                Event = "DirectoryListNew",
                Argument = "0",
            },
            new DialogControlEvent
            {
                Control = "OK",
                Event = "EndDialog",
                Argument = "Return",
            },
            new DialogControlEvent
            {
                Control = "Cancel",
                Event = "EndDialog",
                Argument = "Return",
            });

        return new DialogContent
        {
            Name = DialogName,
            Kind = "Browse",
            FirstControl = "DirectoryList",
            DefaultControl = "OK",
            CancelControl = "Cancel",
            TitleLocKey = "!(loc.Dialog.Browse.Title)",
            Events = events,
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ContentArea",
                    Controls = ImmutableArray.Create(
                        new PlacedControl
                        {
                            Name = "PathLabel",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.Browse.PathLabel)",
                            OverrideX = 10,
                            OverrideY = 6,
                            OverrideWidth = 220,
                            OverrideHeight = 10,
                        },
                        new PlacedControl
                        {
                            Name = "PathEdit",
                            Type = "PathEdit",
                            Property = "_BrowseProperty",
                            OverrideX = 10,
                            OverrideY = 18,
                            OverrideWidth = 220,
                            OverrideHeight = 18,
                        },
                        new PlacedControl
                        {
                            Name = "DirectoryList",
                            Type = "DirectoryList",
                            Property = "_BrowseProperty",
                            OverrideX = 10,
                            OverrideY = 40,
                            OverrideWidth = 220,
                            OverrideHeight = 180,
                        },
                        new PlacedControl
                        {
                            Name = "Up",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Button.Up)",
                            OverrideX = 10,
                            OverrideY = 225,
                            OverrideWidth = 56,
                            OverrideHeight = 17,
                        },
                        new PlacedControl
                        {
                            Name = "NewFolder",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Button.NewFolder)",
                            OverrideX = 70,
                            OverrideY = 225,
                            OverrideWidth = 80,
                            OverrideHeight = 17,
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
                            Name = "OK",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Button.OK)",
                        }),
                }),
        };
    }
}
