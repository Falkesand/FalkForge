using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Shared building blocks for the standard wizard-dialog footer: the BottomLine separator and
/// the Cancel/Next/Back push buttons, plus their conventional event wiring.
/// </summary>
/// <remarks>
/// Every full-canvas wizard dialog (Welcome, License, InstallDir, Customize, Progress, Exit,
/// SetupType, InstallScope) places an identical BottomLine separator above its ButtonRow, and
/// most reuse the same Cancel/Next/Back control shapes and NewDialog/SpawnDialog wiring verbatim.
/// The two modal dialogs (Cancel, Browse) use a smaller canvas with no BottomLine and end the
/// dialog directly instead of spawning/routing through a <see cref="DialogFlowContext"/>, so they
/// are not built from these helpers.
/// </remarks>
internal static class DialogFooter
{
    /// <summary>The BottomLine separator region, identical across every full-canvas wizard dialog.</summary>
    public static RegionPlacement BottomLine() => new()
    {
        RegionName = "BottomLine",
        Controls = ImmutableArray.Create(
            new PlacedControl
            {
                Name = "BottomLine",
                Type = "Line",
            }),
    };

    /// <summary>The standard Cancel push button.</summary>
    public static PlacedControl CancelButton() => new()
    {
        Name = "Cancel",
        Type = "PushButton",
        TextOrLocKey = "!(loc.Button.Cancel)",
    };

    /// <summary>The standard Next push button.</summary>
    public static PlacedControl NextButton() => new()
    {
        Name = "Next",
        Type = "PushButton",
        TextOrLocKey = "!(loc.Button.Next)",
    };

    /// <summary>The standard Back push button.</summary>
    public static PlacedControl BackButton() => new()
    {
        Name = "Back",
        Type = "PushButton",
        TextOrLocKey = "!(loc.Button.Back)",
    };

    /// <summary>
    /// The standard Cancel wiring: SpawnDialog to <see cref="DialogFlowContext.CancelDialog"/>.
    /// Used by every non-modal wizard dialog.
    /// </summary>
    public static DialogControlEvent CancelEvent(DialogFlowContext flow) => new()
    {
        Control = "Cancel",
        Event = "SpawnDialog",
        Argument = flow.CancelDialog,
    };

    /// <summary>
    /// The standard Next wiring: NewDialog to <see cref="DialogFlowContext.NextDialog"/>, falling
    /// back to <paramref name="defaultTarget"/> when the flow leaves it unset (Customize defaults
    /// to "ProgressDlg"; everything else defaults to empty).
    /// </summary>
    public static DialogControlEvent NextEvent(DialogFlowContext flow, string defaultTarget = "") => new()
    {
        Control = "Next",
        Event = "NewDialog",
        Argument = flow.NextDialog ?? defaultTarget,
    };

    /// <summary>
    /// The standard Back wiring: NewDialog to <see cref="DialogFlowContext.BackDialog"/>, falling
    /// back to <paramref name="defaultTarget"/> when the flow leaves it unset (SetupType defaults
    /// to "LicenseAgreementDlg"; everything else defaults to empty).
    /// </summary>
    public static DialogControlEvent BackEvent(DialogFlowContext flow, string defaultTarget = "") => new()
    {
        Control = "Back",
        Event = "NewDialog",
        Argument = flow.BackDialog ?? defaultTarget,
    };
}
