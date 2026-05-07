using System;
using System.Collections.Generic;
using System.Collections.Immutable;

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

    /// <summary>Sets the path to a 493x312 banner image used by every dialog header.</summary>
    public DialogCustomization BannerBitmap(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _bannerBitmap = path;
        return this;
    }

    /// <summary>Sets the path to a 493x312 background bitmap used by Welcome / Exit dialogs.</summary>
    public DialogCustomization DialogBitmap(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _dialogBitmap = path;
        return this;
    }

    /// <summary>Sets the path to a 16x16 header icon shown next to the dialog title.</summary>
    public DialogCustomization HeaderIcon(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _headerIcon = path;
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

    /// <summary>Suppresses a stock dialog from the generated dialog set. Idempotent.</summary>
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
