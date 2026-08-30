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

    // The composer's standard default is Visible|Modal|Minimize|TrackDiskSpace (0x27). This
    // dialog clears Modal (0x2), giving 0x25. Windows Installer runs a modal dialog in
    // InstallUISequence as a blocking message loop that ends only when a control fires
    // EndDialog. This dialog has no such control, because the user is not meant to dismiss it,
    // so authored modal the sequence parks here and never reaches ExecuteAction at 1300 and the
    // install never starts. Modeless, it paints and returns and the sequence carries on.
    private const int AttributesWithoutModal = 0x25;

    /// <summary>Builds the declarative content for the Progress dialog with default flow context.</summary>
    public static DialogContent Build() => Build(new DialogFlowContext());

    /// <summary>Builds the declarative content for the Progress dialog with explicit flow context.</summary>
    /// <param name="flow">Carries the cancel-dialog target for the Cancel SpawnDialog event.</param>
    public static DialogContent Build(DialogFlowContext flow)
    {
        System.ArgumentNullException.ThrowIfNull(flow);

        var events = ImmutableArray.Create(DialogFooter.CancelEvent(flow));

        // Mirrors legacy: ProgressBar tracks SetProgress, ActionText tracks ActionText.
        var eventMappings = ImmutableArray.Create(
            new DialogEventMapping
            {
                Control = "ProgressBar",
                Event = "SetProgress",
                Attribute = "Progress",
            },
            new DialogEventMapping
            {
                Control = "ActionText",
                Event = "ActionText",
                Attribute = "Text",
            });

        return new DialogContent
        {
            Name = DialogName,
            Kind = "Progress",
            AttributesOverride = AttributesWithoutModal,
            FirstControl = "Cancel",
            DefaultControl = "Cancel",
            CancelControl = "Cancel",
            TitleLocKey = "[ProductName] Setup",
            Events = events,
            EventMappings = eventMappings,
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
                DialogFooter.BannerLine(),
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
                DialogFooter.BottomLine(),
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(DialogFooter.CancelButton()),
                }),
        };
    }
}
