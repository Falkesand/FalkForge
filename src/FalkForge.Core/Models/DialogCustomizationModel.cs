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
    /// <summary>Path to a 493x58 banner image (BMP/PNG) shown on the top of every interior dialog.</summary>
    public string? BannerBitmap { get; init; }

    /// <summary>Path to a 493x312 background bitmap for Welcome/Exit dialogs.</summary>
    public string? DialogBitmap { get; init; }

    /// <summary>Path to a header icon (16x16) shown next to the dialog title.</summary>
    public string? HeaderIcon { get; init; }

    /// <summary>Override window title for the installer wizard.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>Per-button label overrides keyed by <see cref="DialogButton"/>.</summary>
    public ImmutableDictionary<DialogButton, string> ButtonLabelOverrides { get; init; }
        = ImmutableDictionary<DialogButton, string>.Empty;

    /// <summary>Set of stock dialogs to suppress entirely.</summary>
    public ImmutableHashSet<StockDialog> SuppressedDialogs { get; init; }
        = ImmutableHashSet<StockDialog>.Empty;

    /// <summary>
    /// Ordered list of extension-contributed dialog steps to insert into the flow.
    /// Each entry names a registered <c>IDialogStepBuilder</c> and the stock dialog
    /// after which the step should be inserted.
    /// Validated at compile time: DLG001 rejects unknown step names; DLG002 rejects
    /// suppressions that break the navigation chain.
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
