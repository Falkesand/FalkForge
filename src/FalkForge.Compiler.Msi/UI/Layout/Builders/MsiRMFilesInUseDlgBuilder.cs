using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock <c>MsiRMFilesInUse</c> dialog.
/// </summary>
/// <remarks>
/// Unlike every other dialog in this namespace, <c>MsiRMFilesInUse</c> is not part of the wizard
/// flow: the Windows Installer engine creates it directly at InstallValidate when Restart Manager
/// reports files in use at Full UI (see
/// <see href="https://learn.microsoft.com/windows/win32/msi/restart-manager">Restart Manager</see>).
/// It is reachable from no other dialog, so there is no <see cref="DialogFlowContext"/> to route
/// through — the modal is self-contained, matching <see cref="CancelDlgBuilder"/>.
/// <para>
/// Geometry mirrors WiX's own <c>UI/wixlib/MsiRMFilesInUse.wxs</c> verbatim: the 370x270 canvas,
/// the OK/Cancel button positions (240/304), and the ContentArea coordinates for Description,
/// Text, the process List, and the ShutdownOption radio group. <c>List</c> is a plain
/// <c>ListBox</c> bound to the built-in <c>FileInUseProcess</c> property — the Windows Installer
/// engine populates it at runtime with the in-use process names; no <c>ListBox</c> table rows are
/// authored here.
/// </para>
/// <para>
/// <b>Event ordering deviates from WiX on purpose.</b> WiX's OK button publishes
/// <c>EndDialog</c> before <c>RMShutdownAndRestart</c>; this builder inverts it so
/// <c>RMShutdownAndRestart</c> fires first (Ordering 1) and <c>EndDialog</c> second (Ordering 2).
/// Per the ControlEvent table docs, "the installer starts each event in the order specified in
/// the Ordering column" — RM-then-end is the reading that matches that sentence literally, while
/// WiX's end-then-RM ordering only works because MSI defers dialog teardown until the sequence
/// finishes. Do not "fix" this back to WiX order without re-reading this remark.
/// </para>
/// <para>
/// <see cref="DialogContent.AttributesOverride"/> adds <c>KeepModeless</c> (0x10) on top of the
/// composer's standard default (0x27), giving 0x37: this dialog is popped by the installer engine
/// itself mid-sequence, not by a sibling dialog's SpawnDialog, so it must not tear down whatever
/// modeless dialog is already on screen underneath it.
/// </para>
/// </remarks>
internal static class MsiRMFilesInUseDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "MsiRMFilesInUse";

    /// <summary>The MSI property this dialog's ShutdownOption radio group is bound to.</summary>
    public const string OptionProperty = "FalkForgeRMOption";

    /// <summary>Radio button value selecting "close and restart the applications".</summary>
    public const string UseRestartManagerValue = "UseRM";

    /// <summary>Radio button value selecting "do not close the applications".</summary>
    public const string DoNotUseRestartManagerValue = "DontUseRM";

    // KeepModeless (0x10) added on top of the composer's standard Visible|Modal|Minimize|
    // TrackDiskSpace default (0x27) — see the <remarks> above for why this dialog needs it.
    private const int AttributesWithKeepModeless = 0x37;

    /// <summary>Builds the declarative content for the MsiRMFilesInUse modal.</summary>
    public static DialogContent Build()
    {
        var events = ImmutableArray.Create(
            new DialogControlEvent
            {
                Control = "OK",
                Event = MsiControlEvent.RMShutdownAndRestart.Value,
                Argument = "0",
                Condition = $"{OptionProperty}~=\"{UseRestartManagerValue}\"",
                Order = 1,
            },
            new DialogControlEvent
            {
                Control = "OK",
                Event = "EndDialog",
                Argument = "Return",
                Order = 2,
            },
            new DialogControlEvent
            {
                Control = "Cancel",
                Event = "EndDialog",
                Argument = "Exit",
                Order = 1,
            });

        var radioButtons = ImmutableArray.Create(
            new DialogRadioButton
            {
                Property = OptionProperty,
                Order = 1,
                Value = UseRestartManagerValue,
                X = 0,
                Y = 0,
                Width = 295,
                Height = 16,
                TextOrLocKey = "!(loc.Dialog.RestartManager.CloseApps)",
            },
            new DialogRadioButton
            {
                Property = OptionProperty,
                Order = 2,
                Value = DoNotUseRestartManagerValue,
                X = 0,
                Y = 20,
                Width = 295,
                Height = 16,
                TextOrLocKey = "!(loc.Dialog.RestartManager.DontCloseApps)",
            });

        return new DialogContent
        {
            Name = DialogName,
            Kind = "RestartManager",
            FirstControl = "OK",
            DefaultControl = "OK",
            CancelControl = "Cancel",
            TitleLocKey = "[ProductName] Setup",
            AttributesOverride = AttributesWithKeepModeless,
            Events = events,
            RadioButtons = radioButtons,
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "TitleRow",
                    Controls = ImmutableArray.Create(
                        new PlacedControl
                        {
                            Name = "Title",
                            Type = "Text",
                            TextOrLocKey = "{\\DlgFontBold8}!(loc.Dialog.RestartManager.Title)",
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
                            TextOrLocKey = "!(loc.Dialog.RestartManager.Description)",
                            OverrideX = 20,
                            OverrideY = 23,
                            OverrideWidth = 280,
                            OverrideHeight = 20,
                        },
                        new PlacedControl
                        {
                            Name = "Text",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.RestartManager.Text)",
                            OverrideX = 20,
                            OverrideY = 55,
                            OverrideWidth = 330,
                            OverrideHeight = 45,
                        },
                        new PlacedControl
                        {
                            Name = "List",
                            Type = "ListBox",
                            Property = "FileInUseProcess",
                            OverrideX = 20,
                            OverrideY = 100,
                            OverrideWidth = 330,
                            OverrideHeight = 80,
                        },
                        new PlacedControl
                        {
                            Name = "ShutdownOption",
                            Type = "RadioButtonGroup",
                            Property = OptionProperty,
                            OverrideX = 26,
                            OverrideY = 190,
                            OverrideWidth = 305,
                            OverrideHeight = 45,
                        }),
                },
                DialogFooter.BottomLine(),
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(
                        DialogFooter.CancelButton(),
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
