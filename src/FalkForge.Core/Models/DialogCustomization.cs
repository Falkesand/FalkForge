using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace FalkForge.Models;

/// <summary>
/// Mutable fluent builder for <see cref="DialogCustomizationModel"/>. Use through
/// <see cref="FalkForge.Builders.PackageBuilder.UseDialogSet(MsiDialogSet, Action{DialogCustomization})"/>.
/// The builder freezes into an immutable <see cref="DialogCustomizationModel"/> when
/// the package is built.
/// </summary>
public sealed class DialogCustomization
{
    private readonly Dictionary<DialogButton, string> _buttonLabels = [];
    private readonly HashSet<StockDialog> _suppressed = [];
    private readonly List<InsertedDialogStep> _insertedSteps = [];
    private string? _bannerBitmap;
    private string? _dialogBitmap;
    private string? _headerIcon;
    private string? _windowTitle;

    /// <summary>
    /// Sets the Binary stream key of a banner image shown on every interior dialog header. For the
    /// stock dialogs — none of which place a bitmap control of their own — this always synthesizes
    /// a new <c>BannerBmp</c> control sized to the layout's 370x44 Installer Unit <c>Banner</c>
    /// region (matching WiX's own <c>BannerBitmap</c>/<c>BannerLine</c> convention): 493x58 px, the
    /// classic Windows Installer banner pixel size for that same region. Installer Units are
    /// approximately 1/12 the height of the 10-point MS Sans Serif font (see the MSI Dialog Table
    /// reference), not Windows dialog units — the two are different concepts despite the similar
    /// name; 4/3 is only an approximation of the ratio between Installer Units and pixels at
    /// typical rendering, not an exact defined conversion. If an interior dialog step already
    /// declares its own Bitmap control (an extension-contributed custom dialog inserted via
    /// <see cref="InsertStep(string, StockDialog)"/> can do this), that existing control's
    /// <c>Text</c> is swapped to this key instead — no control is synthesized, and the banner takes
    /// that control's own dimensions rather than 493x58. The key must name a stream registered via
    /// <see cref="FalkForge.Builders.PackageBuilder.Binary(string, string)"/> — DLG003
    /// fails the build if no matching Binary entry exists.
    /// </summary>
    public DialogCustomization BannerBitmap(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _bannerBitmap = key;
        return this;
    }

    /// <summary>
    /// Sets the Binary stream key of the Welcome/Exit background bitmap. Pixel dimensions
    /// follow the classic MSI convention (roughly 493x312 for the 370x234 Installer Unit dialog
    /// area); the key must name a stream registered via
    /// <see cref="FalkForge.Builders.PackageBuilder.Binary(string, string)"/> — DLG003
    /// fails the build if no matching Binary entry exists.
    /// </summary>
    public DialogCustomization DialogBitmap(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _dialogBitmap = key;
        return this;
    }

    /// <summary>
    /// Sets the Binary stream key of a 16x16 header icon shown next to the dialog title. The
    /// key must name a stream registered via
    /// <see cref="FalkForge.Builders.PackageBuilder.Binary(string, string)"/> — DLG003
    /// fails the build if no matching Binary entry exists.
    /// </summary>
    public DialogCustomization HeaderIcon(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _headerIcon = key;
        return this;
    }

    /// <summary>Overrides the wizard window title.</summary>
    public DialogCustomization WindowTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _windowTitle = title;
        return this;
    }

    /// <summary>Overrides the label for a specific dialog button. Last call wins.</summary>
    public DialogCustomization OverrideButtonLabel(DialogButton button, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _buttonLabels[button] = label;
        return this;
    }

    /// <summary>
    /// NOT IMPLEMENTED. Nothing downstream consumes <see cref="DialogCustomizationModel.SuppressedDialogs"/>:
    /// <see cref="FalkForge.Compiler.Msi.UI.Layout.DialogComposer"/> explicitly declines to apply
    /// suppression (see its remarks), and no dialog-set template skips composing a suppressed
    /// <see cref="StockDialog"/> — every stock dialog is always emitted regardless of this call.
    /// Gated with <c>error: true</c> rather than deleted, or implemented, so the reusable
    /// machinery (the model field, the builder plumbing, the compile-time validation) survives
    /// for the real implementation tracked as task #44. Populating
    /// <see cref="DialogCustomizationModel.SuppressedDialogs"/> directly via an object initializer
    /// bypasses this method entirely; that path is closed separately by DLG002
    /// (<see cref="FalkForge.Compiler.Msi.UI.DialogCustomizationValidator"/>), which fails the
    /// build on any non-empty set.
    /// </summary>
    [Obsolete(
        "SuppressDialog is not implemented — it has no effect on the compiled MSI (see task #44). " +
        "This call is rejected at compile time so it cannot be mistaken for a working feature.",
        error: true)]
    [SuppressMessage("Sonar", "S1133",
        Justification = "Deliberate permanent gate, not a deprecation in progress — see task #24 " +
            "(gate the inert API without deleting it) and task #44 (the real implementation this " +
            "waits for). Removing the reminder is correct only once #44 lands.")]
    public DialogCustomization SuppressDialog(StockDialog dialog)
    {
        _suppressed.Add(dialog);
        return this;
    }

    /// <summary>
    /// Inserts an extension-contributed dialog step after the specified stock dialog.
    /// The step must be registered via the compiler's dialog step registry before compile time.
    /// DLG001 rejects unknown step names at compile time.
    /// </summary>
    /// <param name="stepName">
    /// Stable identifier matching the registered step builder's <c>Name</c> property.
    /// </param>
    /// <param name="after">
    /// The stock dialog after which this step appears. Use <see cref="StockDialog.Extension"/>
    /// to append at the end of the sequence.
    /// </param>
    public DialogCustomization InsertStep(string stepName, StockDialog after)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName);
        _insertedSteps.Add(new InsertedDialogStep(stepName, after));
        return this;
    }

    /// <summary>
    /// Freezes the current builder state into an immutable <see cref="DialogCustomizationModel"/>.
    /// Subsequent mutations of the builder do not affect a previously returned snapshot.
    /// </summary>
    internal DialogCustomizationModel ToModel()
    {
        return new DialogCustomizationModel
        {
            BannerBitmap = _bannerBitmap,
            DialogBitmap = _dialogBitmap,
            HeaderIcon = _headerIcon,
            WindowTitle = _windowTitle,
            ButtonLabelOverrides = _buttonLabels.ToImmutableDictionary(),
            SuppressedDialogs = _suppressed.ToImmutableHashSet(),
            InsertedSteps = _insertedSteps.ToImmutableArray(),
        };
    }
}
