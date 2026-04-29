using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock Cancel confirmation modal.
/// </summary>
/// <remarks>
/// Mirrors the legacy <c>SharedDialogBuilders.BuildCancelDlg</c> control set: a confirmation
/// text block in the ContentArea and Yes/No push buttons in the right-packed ButtonRow.
/// <para>
/// <b>Layout deviation:</b> the legacy modal uses a 260x85 canvas with custom geometry. This
/// builder still targets <see cref="Layouts.Standard370x270"/> because per-template layouts
/// (alternative canvases) are introduced in phase 7+ of the dialog deepening RFC. The output
/// is therefore structurally equivalent to the legacy modal (same controls, same names, same
/// types, same focus/cancel wiring) rather than byte-identical.
/// </para>
/// </remarks>
internal static class CancelDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "CancelDlg";

    /// <summary>Builds the declarative content for the Cancel modal.</summary>
    public static DialogContent Build()
    {
        return new DialogContent
        {
            Name = DialogName,
            Kind = "Cancel",
            FirstControl = "No",
            DefaultControl = "No",
            CancelControl = "No",
            TitleLocKey = "[ProductName] Setup",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ContentArea",
                    Controls = ImmutableArray.Create(
                        new PlacedControl
                        {
                            Name = "Text",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.Cancel.Text)",
                            OverrideX = 48,
                            OverrideY = 15,
                            OverrideWidth = 194,
                            OverrideHeight = 30,
                        }),
                },
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(
                        new PlacedControl
                        {
                            Name = "No",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Button.No)",
                        },
                        new PlacedControl
                        {
                            Name = "Yes",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Button.Yes)",
                        }),
                }),
        };
    }
}
