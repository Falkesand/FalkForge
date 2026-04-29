using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock Progress dialog.
/// </summary>
/// <remarks>
/// Mirrors the legacy <c>SharedDialogBuilders.BuildProgressDlg</c> control set with the status
/// label included: bold title, StatusLabel, ActionText, ProgressBar, BottomLine, and a single
/// Cancel button. The legacy builder also wires SetProgress / ActionText event mappings — those
/// belong to phase 8+ (event customization) and are not part of the declarative content.
/// </remarks>
internal static class ProgressDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "ProgressDlg";

    /// <summary>Builds the declarative content for the Progress dialog.</summary>
    public static DialogContent Build()
    {
        return new DialogContent
        {
            Name = DialogName,
            Kind = "Progress",
            FirstControl = "Cancel",
            DefaultControl = "Cancel",
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
                            TextOrLocKey = "{\\DlgFontBold8}!(loc.Dialog.Progress.Title)",
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
                            Name = "StatusLabel",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.Progress.Status)",
                            OverrideX = 25,
                            OverrideY = 55,
                            OverrideWidth = 50,
                            OverrideHeight = 10,
                        },
                        new PlacedControl
                        {
                            Name = "ActionText",
                            Type = "Text",
                            OverrideX = 75,
                            OverrideY = 55,
                            OverrideWidth = 270,
                            OverrideHeight = 10,
                        },
                        new PlacedControl
                        {
                            Name = "ProgressBar",
                            Type = "ProgressBar",
                            OverrideX = 25,
                            OverrideY = 70,
                            OverrideWidth = 320,
                            OverrideHeight = 10,
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
                        }),
                }),
        };
    }
}
