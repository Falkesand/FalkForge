using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Declarative <see cref="DialogContent"/> builder for the stock InstallScope dialog used by
/// the Advanced template to choose between per-machine and per-user installs.
/// </summary>
/// <remarks>
/// Mirrors the legacy <c>AdvancedDialogTemplate.BuildInstallScopeDlg</c> control set: bold
/// title, description, two large per-scope push buttons (PerMachine / PerUser) with a small
/// description label below each, BottomLine, and Back/Cancel only in the right-packed
/// ButtonRow (Next is omitted because the per-scope buttons themselves advance the wizard).
/// <para>
/// The PerMachine button assigns <c>ALLUSERS=1</c> via a <c>[ALLUSERS]</c> property-set
/// event before advancing, while PerUser clears it with the literal <c>"{}"</c> token (the
/// MSI form for "remove the property"). Both then NewDialog into the next dialog supplied
/// through <see cref="DialogFlowContext.NextDialog"/>.
/// </para>
/// <para>
/// ButtonRow uses the layout default 8 DLU gap; the rightmost button (Cancel at X=304)
/// matches legacy exactly, the Back button is 4 DLU left of legacy. This is the documented
/// phase-6 ButtonRow geometry deviation.
/// </para>
/// </remarks>
internal static class InstallScopeDlgBuilder
{
    /// <summary>The MSI dialog identifier emitted by this builder.</summary>
    public const string DialogName = "InstallScopeDlg";

    /// <summary>Builds the declarative content for the InstallScope dialog with default flow context.</summary>
    public static DialogContent Build() => Build(new DialogFlowContext());

    /// <summary>Builds the declarative content for the InstallScope dialog with explicit flow targets.</summary>
    /// <param name="flow">Back routes to <see cref="DialogFlowContext.BackDialog"/>, the per-scope
    /// buttons advance to <see cref="DialogFlowContext.NextDialog"/>, and Cancel spawns
    /// <see cref="DialogFlowContext.CancelDialog"/>.</param>
    public static DialogContent Build(DialogFlowContext flow)
    {
        System.ArgumentNullException.ThrowIfNull(flow);

        // Mirrors legacy AdvancedDialogTemplate.BuildInstallScopeDlg event set:
        //   Back        NewDialog    -> flow.BackDialog
        //   PerMachine  [ALLUSERS]   = "1"                       (Order 1)
        //   PerMachine  NewDialog    -> flow.NextDialog          (Order 2)
        //   PerUser     [ALLUSERS]   = "{}"                      (Order 1)
        //   PerUser     NewDialog    -> flow.NextDialog          (Order 2)
        //   Cancel      SpawnDialog  -> flow.CancelDialog
        var events = ImmutableArray.Create(
            DialogFooter.BackEvent(flow),
            new DialogControlEvent
            {
                Control = "PerMachine",
                Event = "[ALLUSERS]",
                Argument = "1",
                Order = 1,
            },
            new DialogControlEvent
            {
                Control = "PerMachine",
                Event = "NewDialog",
                Argument = flow.NextDialog ?? string.Empty,
                Order = 2,
            },
            new DialogControlEvent
            {
                Control = "PerUser",
                Event = "[ALLUSERS]",
                Argument = "{}",
                Order = 1,
            },
            new DialogControlEvent
            {
                Control = "PerUser",
                Event = "NewDialog",
                Argument = flow.NextDialog ?? string.Empty,
                Order = 2,
            },
            DialogFooter.CancelEvent(flow));

        return new DialogContent
        {
            Name = DialogName,
            Kind = "InstallScope",
            FirstControl = "PerMachine",
            DefaultControl = "PerMachine",
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
                            TextOrLocKey = "{\\DlgFontBold8}!(loc.Dialog.InstallScope.Title)",
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
                            TextOrLocKey = "!(loc.Dialog.InstallScope.Description)",
                            OverrideX = 25,
                            OverrideY = 23,
                            OverrideWidth = 280,
                            OverrideHeight = 20,
                        },
                        new PlacedControl
                        {
                            Name = "PerMachine",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Dialog.InstallScope.AllUsers)",
                            OverrideX = 40,
                            OverrideY = 75,
                            OverrideWidth = 290,
                            OverrideHeight = 17,
                        },
                        new PlacedControl
                        {
                            Name = "PerMachineDesc",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.InstallScope.AllUsersDesc)",
                            OverrideX = 60,
                            OverrideY = 95,
                            OverrideWidth = 270,
                            OverrideHeight = 20,
                        },
                        new PlacedControl
                        {
                            Name = "PerUser",
                            Type = "PushButton",
                            TextOrLocKey = "!(loc.Dialog.InstallScope.CurrentUser)",
                            OverrideX = 40,
                            OverrideY = 125,
                            OverrideWidth = 290,
                            OverrideHeight = 17,
                        },
                        new PlacedControl
                        {
                            Name = "PerUserDesc",
                            Type = "Text",
                            TextOrLocKey = "!(loc.Dialog.InstallScope.CurrentUserDesc)",
                            OverrideX = 60,
                            OverrideY = 145,
                            OverrideWidth = 270,
                            OverrideHeight = 20,
                        }),
                },
                DialogFooter.BottomLine(),
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(
                        DialogFooter.CancelButton(),
                        DialogFooter.BackButton()),
                }),
        };
    }
}
