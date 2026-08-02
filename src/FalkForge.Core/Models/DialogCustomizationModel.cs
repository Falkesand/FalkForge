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
    /// <c>DialogComposer</c> always synthesizes a new banner control sized to the layout's 370x58
    /// DLU <c>Banner</c> region: as of this release the control area is 493x77 px, converted at the
    /// same 4/3 DLU-to-pixel ratio documented on <see cref="DialogBitmap"/> (493x77, not 493x58: the
    /// region's width and height are both 4/3 of their DLU values, but only the width axis happens
    /// to convert to the familiar 493 — a banner authored at the old 493x58 guidance renders
    /// stretched ~1.33x vertically, since the region currently is 58 DLU / ~77 px tall, not 58 px).
    /// Whether the <c>Banner</c> region's 58 DLU height is itself correct — versus the classic
    /// WiX/MSI banner convention of 44 DLU / 58 px — is under review; if it changes, this documented
    /// 493x77 figure changes with it. If an interior dialog step already declares its own Bitmap
    /// control (an extension-contributed custom dialog inserted via
    /// <see cref="InsertedDialogStep"/> can do this), <c>DialogComposer</c> swaps that existing
    /// control's text to this key instead of synthesizing a new one, and the banner takes that
    /// control's own dimensions rather than 493x77. Must name a stream registered via
    /// <c>PackageBuilder.Binary(name, sourcePath)</c> — DLG003 fails the build if the key does not
    /// resolve to a registered Binary.
    /// </summary>
    public string? BannerBitmap { get; init; }

    /// <summary>
    /// Binary stream key of the Welcome/Exit background bitmap. The synthesized control is
    /// sized to the dialog layout's DLU bounds (370x234 in the stock 370x270 layout); ~493x312
    /// is the equivalent classic MSI pixel convention for that same area. Must name a stream
    /// registered via <c>PackageBuilder.Binary(name, sourcePath)</c> — DLG003 fails the build if
    /// the key does not resolve to a registered Binary.
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
public readonly record struct InsertedDialogStep(string StepName, StockDialog After);

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
