using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock License agreement dialog.
/// </summary>
/// <remarks>
/// Mirrors the legacy <c>SharedDialogBuilders.BuildLicenseAgreementDlg</c> control set: a bold
/// title, a scrollable license text view bound to the LicenseText property, an Accept checkbox
/// bound to LicenseAccepted, the BottomLine separator, and Back/Next/Cancel buttons in the
/// right-packed ButtonRow. ButtonRow geometry uses the layout's default 8 DLU gap so the
/// non-rightmost buttons land 4 DLU left of the legacy positions; rightmost (Cancel at X=304)
/// matches exactly.
/// </remarks>
internal static class LicenseDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "LicenseAgreementDlg";

    /// <summary>Builds the declarative content for the License dialog.</summary>
    public static DialogContent Build()
    {
        return new DialogContent
        {
            Name = DialogName,
            Kind = "License",
            FirstControl = "LicenseText",
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
                            TextOrLocKey = "{\\DlgFontBold8}!(loc.Dialog.License.Title)",
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
                            Name = "LicenseText",
                            Type = "ScrollableText",
                            Property = "LicenseText",
                            OverrideX = 20,
                            OverrideY = 60,
                            OverrideWidth = 330,
                            OverrideHeight = 140,
                        },
                        new PlacedControl
                        {
                            Name = "LicenseAccepted",
                            Type = "CheckBox",
                            Property = "LicenseAccepted",
                            TextOrLocKey = "!(loc.Dialog.License.Accept)",
                            OverrideX = 20,
                            OverrideY = 207,
                            OverrideWidth = 330,
                            OverrideHeight = 18,
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
