using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock Welcome dialog.
/// </summary>
/// <remarks>
/// Mirrors the legacy <c>SharedDialogBuilders.BuildWelcomeDlg</c> control set: a bold title
/// in the TitleRow, a description block in the ContentArea, the standard BottomLine separator,
/// and Next/Cancel buttons in the right-packed ButtonRow. ButtonRow geometry uses the layout's
/// default 8 DLU gap and so produces structurally-equivalent (but not byte-identical) coordinates
/// compared with the legacy hand-coded gaps; the rightmost button (Cancel at X=304) still matches.
/// </remarks>
internal static class WelcomeDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "WelcomeDlg";

    /// <summary>Builds the declarative content for the Welcome dialog with default flow context.</summary>
    public static DialogContent Build() => Build(new DialogFlowContext());

    /// <summary>Builds the declarative content for the Welcome dialog with explicit flow targets.</summary>
    /// <param name="flow">Navigation targets for the Next and Cancel events.</param>
    public static DialogContent Build(DialogFlowContext flow)
    {
        System.ArgumentNullException.ThrowIfNull(flow);

        var events = ImmutableArray.Create(
            DialogFooter.NextEvent(flow),
            DialogFooter.CancelEvent(flow));

        return new DialogContent
        {
            Name = DialogName,
            Kind = "Welcome",
            FirstControl = "Next",
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
                            TextOrLocKey = "{\\DlgFontBold8}!(loc.Dialog.Welcome.Title)",
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
                            TextOrLocKey = "!(loc.Dialog.Welcome.DescriptionFull)",
                            OverrideX = 25,
                            OverrideY = 23,
                            OverrideWidth = 280,
                            OverrideHeight = 40,
                        }),
                },
                DialogFooter.BottomLine(),
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(
                        DialogFooter.CancelButton(),
                        DialogFooter.NextButton()),
                }),
        };
    }
}
