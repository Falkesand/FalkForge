using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock Exit / Complete dialog.
/// </summary>
/// <remarks>
/// Mirrors the legacy <c>SharedDialogBuilders.BuildExitDlg</c> control set: bold title,
/// description, BottomLine separator, and a single Finish button. The legacy
/// <see cref="DialogContent.CancelControl"/> is intentionally "Finish" so pressing Escape
/// completes the wizard rather than aborting it.
/// </remarks>
internal static class ExitDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "ExitDlg";

    /// <summary>Builds the declarative content for the Exit dialog.</summary>
    public static DialogContent Build()
    {
        return new DialogContent
        {
            Name = DialogName,
            Kind = "Exit",
            FirstControl = "Finish",
            DefaultControl = "Finish",
            CancelControl = "Finish",
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
                            TextOrLocKey = "{\\DlgFontBold8}!(loc.Dialog.Complete.Title)",
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
                            TextOrLocKey = "!(loc.Dialog.Complete.Description)",
                            OverrideX = 25,
                            OverrideY = 23,
                            OverrideWidth = 280,
                            OverrideHeight = 20,
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
                            Name = "Finish",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Button.Finish)",
                        }),
                }),
        };
    }
}
