using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock Customize / feature-tree dialog.
/// </summary>
/// <remarks>
/// Mirrors the legacy <c>SharedDialogBuilders.BuildCustomizeDlg</c> control set: bold title,
/// description, SelectionTree (bound to _BrowseProperty), ItemDescription/ItemSize text panels,
/// VolumeCostList disk-cost grid, BottomLine, and Back/Next/Cancel in the right-packed ButtonRow.
/// ButtonRow gap is the layout default 8 DLU; non-rightmost button positions are structurally
/// equivalent rather than byte-identical with the legacy builder.
/// </remarks>
internal static class CustomizeDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "CustomizeDlg";

    /// <summary>Builds the declarative content for the Customize dialog.</summary>
    public static DialogContent Build()
    {
        return new DialogContent
        {
            Name = DialogName,
            Kind = "Customize",
            FirstControl = "Tree",
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
                            TextOrLocKey = "{\\DlgFontBold8}!(loc.Dialog.Customize.Title)",
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
                            TextOrLocKey = "!(loc.Dialog.Customize.Description)",
                            OverrideX = 25,
                            OverrideY = 23,
                            OverrideWidth = 280,
                            OverrideHeight = 15,
                        },
                        new PlacedControl
                        {
                            Name = "Tree",
                            Type = "SelectionTree",
                            Property = "_BrowseProperty",
                            OverrideX = 25,
                            OverrideY = 55,
                            OverrideWidth = 175,
                            OverrideHeight = 130,
                        },
                        new PlacedControl
                        {
                            Name = "ItemDescription",
                            Type = "Text",
                            OverrideX = 210,
                            OverrideY = 55,
                            OverrideWidth = 140,
                            OverrideHeight = 50,
                        },
                        new PlacedControl
                        {
                            Name = "ItemSize",
                            Type = "Text",
                            OverrideX = 210,
                            OverrideY = 110,
                            OverrideWidth = 140,
                            OverrideHeight = 15,
                        },
                        new PlacedControl
                        {
                            Name = "DiskCost",
                            Type = "VolumeCostList",
                            OverrideX = 25,
                            OverrideY = 195,
                            OverrideWidth = 320,
                            OverrideHeight = 30,
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
