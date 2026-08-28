using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout.Builders;

/// <summary>
/// Shared building blocks for the standard wizard dialog: the BannerLine and BottomLine
/// separators and the Cancel/Next/Back push buttons, plus their conventional event wiring.
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

    /// <summary>
    /// The BannerLine separator region, sitting at the Banner region's own bottom edge. Used by
    /// interior wizard-page dialogs only (License, InstallDir, Customize, SetupType, InstallScope,
    /// Progress, MsiRMFilesInUse) — the exterior Welcome/Exit dialogs paint a full-canvas
    /// <c>DialogBitmap</c> background instead of a distinct <c>Banner</c> strip, so a line here
    /// would cut across that artwork rather than separating anything.
    /// </summary>
    public static RegionPlacement BannerLine() => new()
    {
        RegionName = "BannerLine",
        Controls = ImmutableArray.Create(
            new PlacedControl
            {
                Name = "BannerLine",
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
    /// to "ProgressDlg"; everything else defaults to empty). When the resolved target is
    /// <c>ProgressDlg</c>, this returns <see cref="InstallEvent"/> instead of a NewDialog event;
    /// see that method's remarks for why. See ledger D64.
    /// </summary>
    public static DialogControlEvent NextEvent(DialogFlowContext flow, string defaultTarget = "")
    {
        var target = flow.NextDialog ?? defaultTarget;
        return target == "ProgressDlg"
            ? InstallEvent("Next")
            : new DialogControlEvent
            {
                Control = "Next",
                Event = "NewDialog",
                Argument = target,
            };
    }

    /// <summary>
    /// The wiring for a control that hands the wizard off to <c>InstallUISequence</c> so
    /// <c>ProgressDlg</c> (sequence 1200, modeless) and <c>ExecuteAction</c> (sequence 1300, the
    /// action that actually performs the install) can run.
    /// </summary>
    /// <remarks>
    /// <c>ProgressDlg</c> is not another wizard page to reach with <c>NewDialog</c>. Doing that
    /// opens it as a second modal dialog nested inside the current dialog action, which never
    /// ends, so the sequence never advances past the current dialog's own sequence number and
    /// <c>ExecuteAction</c> never runs, so the install never starts. Ending the dialog with
    /// <c>EndDialog</c>/<c>Return</c> instead returns control to <c>InstallUISequence</c>, which
    /// then runs 1200 and 1300 on its own. See ledger D64, and
    /// <c>InstallDirDlgBuilder</c>'s Next event, which already did this correctly.
    /// </remarks>
    public static DialogControlEvent InstallEvent(string control) => new()
    {
        Control = control,
        Event = "EndDialog",
        Argument = "Return",
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
