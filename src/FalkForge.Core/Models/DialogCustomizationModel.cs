using System.Collections.Immutable;

namespace FalkForge.Models;

/// <summary>
/// Immutable customization model applied to the stock MSI dialog templates. Branding
/// (banner, dialog bitmap, header icon, window title), per-button label overrides,
/// per-stock-dialog suppression, and extension step insertions are plumbed through this
/// record. The composer reads these values when emitting the final <see cref="MsiDialogModel"/> set.
/// </summary>
public sealed record DialogCustomizationModel
{
    /// <summary>
    /// Binary stream key of a banner image (BMP/PNG) shown on the top of every interior dialog.
    /// For the stock dialogs — none of which declare a bitmap control of their own —
    /// <c>DialogComposer</c> always synthesizes a new banner control sized to the layout's 370x44
    /// Installer Unit <c>Banner</c> region (matching WiX's own <c>BannerBitmap</c>/<c>BannerLine</c>
    /// convention): the control area is 493x58 px, the classic Windows Installer banner pixel size
    /// for that same region. Installer Units are approximately 1/12 the height of the 10-point MS
    /// Sans Serif font (see the MSI Dialog Table reference), not Windows dialog units — the two are
    /// different concepts despite the similar name; 4/3 is only an approximation of the ratio
    /// between Installer Units and pixels at typical rendering, not an exact defined conversion.
    /// If an interior dialog step already declares its own Bitmap control (an extension-contributed
    /// custom dialog inserted via <see cref="InsertedDialogStep"/> can do this), <c>DialogComposer</c>
    /// swaps that existing control's text to this key instead of synthesizing a new one, and the
    /// banner takes that control's own dimensions rather than 493x58. Must name a stream registered
    /// via <c>PackageBuilder.Binary(name, sourcePath)</c> — DLG003 fails the build if the key does
    /// not resolve to a registered Binary.
    /// </summary>
    public string? BannerBitmap { get; init; }

    /// <summary>
    /// Binary stream key of the Welcome/Exit background bitmap. The synthesized control is
    /// sized to the dialog layout's Installer Unit bounds (370x234 in the stock 370x270 layout);
    /// ~493x312 is the equivalent classic MSI pixel convention for that same area. Must name a
    /// stream registered via <c>PackageBuilder.Binary(name, sourcePath)</c> — DLG003 fails the
    /// build if the key does not resolve to a registered Binary.
    /// </summary>
    public string? DialogBitmap { get; init; }

    /// <summary>
    /// Binary stream key of a header icon (16x16) shown next to the dialog title. Must name a
    /// stream registered via <c>PackageBuilder.Binary(name, sourcePath)</c> — DLG003 fails the
    /// build if the key does not resolve to a registered Binary.
    /// </summary>
    public string? HeaderIcon { get; init; }

    /// <summary>Override window title for the installer wizard.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>Per-button label overrides keyed by <see cref="DialogButton"/>.</summary>
    public ImmutableDictionary<DialogButton, string> ButtonLabelOverrides { get; init; }
        = ImmutableDictionary<DialogButton, string>.Empty;

    /// <summary>
    /// NOT IMPLEMENTED (task #44) — no dialog-set emitter or <c>DialogComposer</c> consumes this
    /// set, so a populated entry compiles into an MSI that shows the dialog anyway.
    /// <see cref="DialogCustomization.SuppressDialog"/> is gated with
    /// <c>[Obsolete(error: true)]</c>, but this is a public <c>init</c> property: an author can
    /// still populate it directly via an object initializer without ever calling that method.
    /// DLG002 (<see cref="FalkForge.Compiler.Msi.UI.DialogCustomizationValidator"/>) closes that
    /// second path by failing the build whenever this set is non-empty, regardless of how it was
    /// populated. Kept non-empty-capable (rather than removed) so the field, and everything that
    /// reads it, remain available for task #44's real implementation.
    /// </summary>
    public ImmutableHashSet<StockDialog> SuppressedDialogs { get; init; }
        = ImmutableHashSet<StockDialog>.Empty;

    /// <summary>
    /// Ordered list of extension-contributed dialog steps to insert into the flow.
    /// Each entry names a registered <c>IDialogStepBuilder</c> and the stock dialog
    /// after which the step should be inserted.
    /// Validated at compile time: DLG001 rejects unknown step names; DLG002 rejects any
    /// non-empty <see cref="SuppressedDialogs"/> (the feature is not implemented — see task #44).
    /// </summary>
    public ImmutableArray<InsertedDialogStep> InsertedSteps { get; init; }
        = ImmutableArray<InsertedDialogStep>.Empty;
}

/// <summary>
/// Describes a single extension-contributed dialog step insertion: the step builder name
/// and the stock dialog after which the step should appear in the wizard sequence.
/// </summary>
/// <param name="StepName">
/// Stable identifier that matches the <c>Name</c> property of the registered step builder.
/// Validated at compile time by DLG001.
/// </param>
/// <param name="After">
/// The stock dialog after which this step is inserted. Use <see cref="StockDialog.Extension"/>
/// to append at the end of the sequence.
/// </param>
public readonly record struct InsertedDialogStep(string StepName, DialogStepAnchor After);

/// <summary>Buttons whose labels can be overridden via <see cref="DialogCustomizationModel.ButtonLabelOverrides"/>.</summary>
public enum DialogButton
{
    Next,
    Back,
    Cancel,
    Install,
    Finish,
    Browse,
    Print,
    Remove,
    Repair,
}

/// <summary>
/// Dialogs an extension-contributed step can be inserted after, via
/// <see cref="FalkForge.Models.DialogCustomization.InsertStep(string, DialogStepAnchor)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="StockDialog"/>, which names dialogs that can be SUPPRESSED and does
/// not describe the dialogs a step can follow. Four of its members map to no emitted dialog at all,
/// its <c>Progress</c> member is a modeless dialog with no Next control to splice, its <c>Exit</c>
/// member runs after the install, and the InstallScope dialog has no member. Inserting after any of
/// those was measured to be a silent no-op.
/// </para>
/// <para>
/// The setup-type dialog is deliberately absent here too. It has no Next control and three outgoing
/// edges, because Typical and Complete start the install while Custom goes to feature selection, so
/// a step inserted after it has no single continuation to inherit. Use <see cref="BeforeInstall"/>
/// to put a step in front of the install on those sets, which is well defined however the user
/// branched.
/// </para>
/// </remarks>
public enum DialogStepAnchor
{
    /// <summary>After the Welcome dialog. Present in every stock dialog set.</summary>
    Welcome,

    /// <summary>After the licence agreement dialog. Absent from the Minimal set.</summary>
    License,

    /// <summary>After the install-scope dialog. Present in the Advanced set.</summary>
    InstallScope,

    /// <summary>After the install-directory dialog. Present in the InstallDir, Mondo and Advanced sets.</summary>
    InstallDir,

    /// <summary>After the feature-selection dialog. Absent from the Minimal and InstallDir sets.</summary>
    Features,

    /// <summary>
    /// As the last interactive page before the install starts, whichever dialog that is for the
    /// active set. The set-independent anchor, and the only one that works for every stock set.
    /// </summary>
    BeforeInstall,
}

/// <summary>Stock dialogs that can be suppressed via <see cref="DialogCustomizationModel.SuppressedDialogs"/>.</summary>
public enum StockDialog
{
    Welcome,
    License,
    InstallDir,
    Features,
    Ready,
    Progress,
    Exit,
    Maintenance,
    Extension,
}
