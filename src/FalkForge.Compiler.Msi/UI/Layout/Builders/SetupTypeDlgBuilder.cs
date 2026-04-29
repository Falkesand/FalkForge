using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock SetupType (Mondo) dialog.
/// </summary>
/// <remarks>
/// Mirrors the legacy <c>SharedDialogBuilders.BuildSetupTypeDlg</c> control set: bold title,
/// description, three large setup-type push buttons (Typical / Custom / Complete) with matching
/// description text below each, BottomLine, and Back/Cancel only in the right-packed ButtonRow
/// (Next is omitted because the setup-type buttons themselves advance the wizard). ButtonRow
/// uses the layout default 8 DLU gap; the rightmost button (Cancel at X=304) matches legacy
/// exactly, the Back button is 4 DLU left of legacy.
/// </remarks>
internal static class SetupTypeDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "SetupTypeDlg";

    /// <summary>Builds the declarative content for the SetupType dialog with default flow context.</summary>
    public static DialogContent Build() => Build(new DialogFlowContext());

    /// <summary>Builds the declarative content for the SetupType dialog with explicit flow targets.</summary>
    /// <param name="flow">Back routes to <see cref="DialogFlowContext.BackDialog"/> (defaults to LicenseAgreementDlg per legacy).
    /// Typical and Complete advance to ProgressDlg, Custom advances to CustomizeDlg.</param>
    public static DialogContent Build(DialogFlowContext flow)
    {
        System.ArgumentNullException.ThrowIfNull(flow);

        var events = ImmutableArray.Create(
            new DialogControlEvent
            {
                Control = "Back",
                Event = "NewDialog",
                Argument = flow.BackDialog ?? "LicenseAgreementDlg",
            },
            new DialogControlEvent
            {
                Control = "TypicalButton",
                Event = "NewDialog",
                Argument = "ProgressDlg",
            },
            new DialogControlEvent
            {
                Control = "CustomButton",
                Event = "NewDialog",
                Argument = "CustomizeDlg",
            },
            new DialogControlEvent
            {
                Control = "CompleteButton",
                Event = "NewDialog",
                Argument = "ProgressDlg",
            },
            new DialogControlEvent
            {
                Control = "Cancel",
                Event = "SpawnDialog",
                Argument = flow.CancelDialog,
            });

        return new DialogContent
        {
            Name = DialogName,
            Kind = "SetupType",
            FirstControl = "TypicalButton",
            DefaultControl = "TypicalButton",
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
                            TextOrLocKey = "{\\DlgFontBold8}!(loc.Dialog.SetupType.Title)",
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
                            TextOrLocKey = "!(loc.Dialog.SetupType.Description)",
                            OverrideX = 25,
                            OverrideY = 23,
                            OverrideWidth = 280,
                            OverrideHeight = 15,
                        },
                        new PlacedControl
                        {
                            Name = "TypicalButton",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Dialog.SetupType.Typical)",
                            OverrideX = 40,
                            OverrideY = 65,
                            OverrideWidth = 290,
                            OverrideHeight = 17,
                        },
                        new PlacedControl
                        {
                            Name = "TypicalDesc",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.SetupType.TypicalDesc)",
                            OverrideX = 60,
                            OverrideY = 85,
                            OverrideWidth = 270,
                            OverrideHeight = 20,
                        },
                        new PlacedControl
                        {
                            Name = "CustomButton",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Dialog.SetupType.Custom)",
                            OverrideX = 40,
                            OverrideY = 115,
                            OverrideWidth = 290,
                            OverrideHeight = 17,
                        },
                        new PlacedControl
                        {
                            Name = "CustomDesc",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.SetupType.CustomDesc)",
                            OverrideX = 60,
                            OverrideY = 135,
                            OverrideWidth = 270,
                            OverrideHeight = 20,
                        },
                        new PlacedControl
                        {
                            Name = "CompleteButton",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Dialog.SetupType.Complete)",
                            OverrideX = 40,
                            OverrideY = 165,
                            OverrideWidth = 290,
                            OverrideHeight = 17,
                        },
                        new PlacedControl
                        {
                            Name = "CompleteDesc",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.SetupType.CompleteDesc)",
                            OverrideX = 60,
                            OverrideY = 185,
                            OverrideWidth = 270,
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
                            Name = "Cancel",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Button.Cancel)",
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
