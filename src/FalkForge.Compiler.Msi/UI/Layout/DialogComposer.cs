using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Composes a declarative <see cref="DialogContent"/> against a <see cref="DialogLayout"/>
/// to produce a concrete <see cref="MsiDialogModel"/>.
/// </summary>
/// <remarks>
/// Phase 5: every <see cref="RegionPlacement"/> in <see cref="DialogContent.Placements"/> is
/// resolved through its region's <see cref="IRegionLayoutPolicy"/>. Resolved bounds are mapped
/// onto <see cref="MsiControlModel"/> entries which preserve the input order.
/// <para>
/// Phase 9: the three-arg overload accepts an optional <see cref="DialogCustomizationModel"/>
/// and applies customization verbs to the produced model: <see cref="DialogCustomizationModel.WindowTitle"/>
/// overrides <see cref="MsiDialogModel.Title"/>, <see cref="DialogCustomizationModel.BannerBitmap"/>
/// rewrites the <c>Text</c> of every other <c>Bitmap</c>-typed control, <see cref="DialogCustomizationModel.HeaderIcon"/>
/// rewrites the <c>Text</c> of every <c>Icon</c>-typed control, and entries in
/// <see cref="DialogCustomizationModel.ButtonLabelOverrides"/> rewrite the <c>Text</c> of
/// the matching <c>PushButton</c> identified through <see cref="DialogButtonNames.Map"/>.
/// Suppression of stock dialogs (<see cref="DialogCustomizationModel.SuppressedDialogs"/>) is
/// not applied here — that is a dialog-set-level concern handled by the emitter that decides
/// which dialogs to compose at all.
/// <see cref="DialogCustomizationModel.DialogBitmap"/> targets the exterior Welcome/Exit
/// dialogs only (<see cref="DialogContent.Kind"/> "Welcome" or "Exit"): a synthetic full-canvas
/// background <c>Bitmap</c> control (the classic 370x234 WixUI_Bmp_Dialog convention) is inserted
/// ahead of every other control so later controls draw in front of it.
/// <see cref="DialogCustomizationModel.BannerBitmap"/> and <see cref="DialogCustomizationModel.HeaderIcon"/>
/// synthesize their controls on interior wizard-page dialogs only — non-exterior dialogs that
/// declare a <c>TitleRow</c> placement (the structural marker distinguishing a full wizard page
/// from a small modal like <c>CancelDlg</c>/<c>BrowseDlg</c>, neither of which places one).
/// Synthesis only happens when the composed dialog has no <c>Bitmap</c>/<c>Icon</c>-typed control
/// of its own yet; when one already exists (e.g. an author-placed control), the existing swap
/// behavior above still rewrites its <c>Text</c> in place — no duplicate control is added.
/// </para>
/// <see cref="MsiDialogModel"/> is internal so this composer is internal as well.
/// </remarks>
internal static class DialogComposer
{
    // Cached single-instance policies — they are stateless, allocation-free reuse.
    private static readonly AbsoluteRegionLayout AbsolutePolicy = new();
    private static readonly RightPackedRegionLayout RightPackedPolicy = new();
    private static readonly TopStackedRegionLayout TopStackedPolicy = new();
    private static readonly SingleControlRegionLayout SingleControlPolicy = new();

    // Frozen lookup keyed by the MSI control type string used in PlacedControl.Type. The
    // member-name spelling is the exact string the Windows Installer Control table expects, so
    // keying off the string keeps the declarative DSL string-typed and the model strongly typed.
    private static readonly FrozenDictionary<string, MsiControlType> ControlTypeLookup = BuildControlTypeLookup();

    /// <summary>
    /// Compose a declarative <see cref="DialogContent"/> against a <paramref name="layout"/>
    /// to produce a concrete <see cref="MsiDialogModel"/>.
    /// </summary>
    public static MsiDialogModel Compose(DialogContent content, DialogLayout layout)
        => Compose(content, layout, customization: null);

    /// <summary>
    /// Compose a declarative <see cref="DialogContent"/> against a <paramref name="layout"/>,
    /// then apply branding and button-label overrides from an optional
    /// <paramref name="customization"/>.
    /// </summary>
    public static MsiDialogModel Compose(DialogContent content, DialogLayout layout, DialogCustomizationModel? customization)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(layout);

        var resolvedFirst = content.FirstControl ?? FindFallbackFirstControl(content);

        var title = !string.IsNullOrEmpty(customization?.WindowTitle)
            ? customization.WindowTitle
            : content.TitleLocKey ?? string.Empty;

        var model = new MsiDialogModel
        {
            Name = content.Name,
            Width = layout.CanvasWidth,
            Height = layout.CanvasHeight,
            Title = title,
            FirstControl = resolvedFirst ?? string.Empty,
            DefaultControl = content.DefaultControl,
            CancelControl = content.CancelControl,
        };

        if (content.Placements.IsDefaultOrEmpty)
        {
            AppendDeclarativeEvents(model, content);
            return model;
        }

        foreach (var placement in content.Placements)
        {
            if (!layout.TryGetRegion(placement.RegionName, out var region))
            {
                throw new InvalidOperationException(
                    $"Region '{placement.RegionName}' is not defined in layout '{layout.Name}'.");
            }

            var controls = placement.Controls;
            if (controls.IsDefaultOrEmpty)
            {
                continue;
            }

            var policy = SelectPolicy(region.Policy);
            var resolved = policy.Resolve(region, controls);

            foreach (var entry in resolved)
            {
                model.Controls.Add(BuildControlModel(entry));
            }
        }

        ApplyCustomization(model, customization, content, layout);

        AppendDeclarativeEvents(model, content);

        return model;
    }

    private static void AppendDeclarativeEvents(MsiDialogModel model, DialogContent content)
    {
        if (!content.Events.IsDefaultOrEmpty)
        {
            foreach (var declarative in content.Events)
            {
                model.Events.Add(new MsiControlEventModel
                {
                    DialogName = content.Name,
                    ControlName = declarative.Control,
                    Event = MsiControlEvent.Parse(declarative.Event),
                    Argument = declarative.Argument,
                    Condition = declarative.Condition ?? "1",
                    Ordering = declarative.Order,
                });
            }
        }

        if (!content.Conditions.IsDefaultOrEmpty)
        {
            foreach (var declarative in content.Conditions)
            {
                model.Conditions.Add(new MsiControlConditionModel
                {
                    DialogName = content.Name,
                    ControlName = declarative.Control,
                    Action = ParseConditionAction(declarative.Action),
                    Condition = declarative.Condition,
                });
            }
        }

        if (!content.EventMappings.IsDefaultOrEmpty)
        {
            foreach (var declarative in content.EventMappings)
            {
                model.EventMappings.Add(new MsiEventMappingModel
                {
                    DialogName = content.Name,
                    ControlName = declarative.Control,
                    Event = declarative.Event,
                    Attribute = declarative.Attribute,
                });
            }
        }
    }

    private static MsiConditionAction ParseConditionAction(string action)
    {
        // Use Ordinal comparisons — MSI ControlCondition Action column is exact-match.
        if (string.Equals(action, "Disable", StringComparison.Ordinal)) return MsiConditionAction.Disable;
        if (string.Equals(action, "Enable", StringComparison.Ordinal)) return MsiConditionAction.Enable;
        if (string.Equals(action, "Hide", StringComparison.Ordinal)) return MsiConditionAction.Hide;
        if (string.Equals(action, "Show", StringComparison.Ordinal)) return MsiConditionAction.Show;
        if (string.Equals(action, "Default", StringComparison.Ordinal)) return MsiConditionAction.Default;

        throw new InvalidOperationException(
            $"Unknown MSI condition action '{action}'. Expected Enable/Disable/Show/Hide/Default.");
    }

    // Canonical MSI control Names for the synthetic controls this method may insert. Excluded by
    // name from each other's sweep below so the three bitmap/icon verbs never clobber each other
    // when several are set together.
    private const string DialogBackgroundBitmapControlName = "DialogBmp";
    private const string BannerBitmapControlName = "BannerBmp";
    private const string HeaderIconControlName = "HeaderIcon";

    private static void ApplyCustomization(
        MsiDialogModel model,
        DialogCustomizationModel? customization,
        DialogContent content,
        DialogLayout layout)
    {
        if (customization is null)
        {
            return;
        }

        // Build a button-name -> override-label map keyed by the canonical MSI control Name.
        // FrozenDictionary lookup keeps the per-control hot loop allocation-free.
        Dictionary<string, string>? buttonOverrides = null;
        if (customization.ButtonLabelOverrides.Count > 0)
        {
            buttonOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in customization.ButtonLabelOverrides)
            {
                if (DialogButtonNames.Map.TryGetValue(pair.Key, out var controlName))
                {
                    buttonOverrides[controlName] = pair.Value;
                }
            }
        }

        var bannerBitmap = customization.BannerBitmap;
        var dialogBitmap = customization.DialogBitmap;
        var headerIcon = customization.HeaderIcon;
        var hasBanner = !string.IsNullOrEmpty(bannerBitmap);
        var hasDialogBitmap = !string.IsNullOrEmpty(dialogBitmap);
        var hasIcon = !string.IsNullOrEmpty(headerIcon);
        var hasButtonOverrides = buttonOverrides is { Count: > 0 };

        if (!hasBanner && !hasDialogBitmap && !hasIcon && !hasButtonOverrides)
        {
            return;
        }

        // DialogBitmap only applies to the exterior Welcome/Exit dialogs, matching the classic
        // WixUI_Bmp_Dialog convention. Stock templates do not declare this control themselves —
        // it is opt-in branding — so it is synthesized here and inserted first (index 0) so
        // subsequent controls (title/description/buttons) draw in front of it, matching MSI's
        // Control-table row-order Z-ordering.
        if (hasDialogBitmap && IsExteriorDialogKind(content.Kind))
        {
            model.Controls.Insert(0, BuildDialogBitmapControl(dialogBitmap!, layout));
        }

        // BannerBitmap/HeaderIcon synthesize their controls on interior wizard pages only, and
        // only when the composed dialog has no Bitmap/Icon control of its own — an author-placed
        // control still gets its Text swapped in place by the sweep below, never duplicated.
        // Checked against the pre-synthesis control list: DialogBitmap synthesis above can never
        // fire on the same dialog as this block (mutually exclusive by IsExteriorDialogKind vs.
        // IsInteriorWizardDialog), so there is no cross-contamination to guard against here.
        if (IsInteriorWizardDialog(content))
        {
            int insertAt = 0;
            if (hasBanner && !ContainsControlType(model.Controls, MsiControlType.Bitmap))
            {
                model.Controls.Insert(insertAt++, BuildBannerBitmapControl(bannerBitmap!, layout));
            }

            if (hasIcon && !ContainsControlType(model.Controls, MsiControlType.Icon))
            {
                // Inserted right after the banner (if any) so the icon draws in front of it.
                model.Controls.Insert(insertAt, BuildHeaderIconControl(headerIcon!, layout));
            }
        }

        foreach (var control in model.Controls)
        {
            if (hasBanner
                && control.Type == MsiControlType.Bitmap
                && !string.Equals(control.Name, DialogBackgroundBitmapControlName, StringComparison.Ordinal))
            {
                control.Text = bannerBitmap;
                continue;
            }

            if (hasIcon && control.Type == MsiControlType.Icon)
            {
                control.Text = headerIcon;
                continue;
            }

            if (hasButtonOverrides
                && control.Type == MsiControlType.PushButton
                && buttonOverrides!.TryGetValue(control.Name, out var label))
            {
                control.Text = label;
            }
        }
    }

    private static bool IsExteriorDialogKind(string dialogKind) =>
        string.Equals(dialogKind, "Welcome", StringComparison.Ordinal)
        || string.Equals(dialogKind, "Exit", StringComparison.Ordinal);

    // Interior wizard pages are every non-exterior dialog that declares a TitleRow placement —
    // the structural marker separating a full wizard page (License, InstallDir, Customize,
    // SetupType, InstallScope, Progress) from a small modal (CancelDlg, BrowseDlg) that never
    // places one. Welcome/Exit also declare TitleRow, so exterior exclusion is checked first.
    private static bool IsInteriorWizardDialog(DialogContent content) =>
        !IsExteriorDialogKind(content.Kind) && HasTitleRowPlacement(content);

    private static bool HasTitleRowPlacement(DialogContent content)
    {
        if (content.Placements.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var placement in content.Placements)
        {
            if (string.Equals(placement.RegionName, "TitleRow", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsControlType(List<MsiControlModel> controls, MsiControlType type)
    {
        // Index-based loop avoids the List<T> enumerator allocation (HAA0401) in this hot path.
        for (int i = 0; i < controls.Count; i++)
        {
            if (controls[i].Type == type)
            {
                return true;
            }
        }

        return false;
    }

    private static MsiControlModel BuildDialogBitmapControl(string dialogBitmapPath, DialogLayout layout)
    {
        // Height stops at the BottomLine region's Y (234 DLU in the stock 370x270 layout) so the
        // background does not paint over the button row; falls back to the full canvas height if
        // a future layout omits that region.
        int height = layout.TryGetRegion("BottomLine", out var bottomLine)
            ? bottomLine.Bounds.Y
            : layout.CanvasHeight;

        return new MsiControlModel
        {
            Name = DialogBackgroundBitmapControlName,
            Type = MsiControlType.Bitmap,
            X = 0,
            Y = 0,
            Width = layout.CanvasWidth,
            Height = height,
            Text = dialogBitmapPath,
        };
    }

    private static MsiControlModel BuildBannerBitmapControl(string bannerBitmapPath, DialogLayout layout)
    {
        // Sized to the layout's own Banner region (370x58 DLU in the stock layout — the same
        // region documented in dialog-template-architecture.md), never hardcoded numbers, so a
        // future per-template layout stays authoritative for this geometry too.
        Rect bounds = layout.TryGetRegion("Banner", out var banner)
            ? banner.Bounds
            : new Rect { X = 0, Y = 0, Width = layout.CanvasWidth, Height = 58 };

        return new MsiControlModel
        {
            Name = BannerBitmapControlName,
            Type = MsiControlType.Bitmap,
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            Text = bannerBitmapPath,
        };
    }

    private static MsiControlModel BuildHeaderIconControl(string headerIconPath, DialogLayout layout)
    {
        // Top-right corner of the Banner region, vertically aligned with TitleRow (Y=6 in the
        // stock layout) — "shown next to the dialog title" per DialogCustomization.HeaderIcon's
        // XML doc. 16x16 matches that same doc's stated icon size; 8 DLU margin matches the
        // ButtonRow's own default gap (RegionDefaults.Gap) used elsewhere in this layout.
        const int IconSize = 16;
        const int Margin = 8;

        Rect bannerBounds = layout.TryGetRegion("Banner", out var banner)
            ? banner.Bounds
            : new Rect { X = 0, Y = 0, Width = layout.CanvasWidth, Height = 58 };
        Rect titleRowBounds = layout.TryGetRegion("TitleRow", out var titleRow)
            ? titleRow.Bounds
            : new Rect { X = 15, Y = 6, Width = 200, Height = 15 };

        return new MsiControlModel
        {
            Name = HeaderIconControlName,
            Type = MsiControlType.Icon,
            X = bannerBounds.X + bannerBounds.Width - IconSize - Margin,
            Y = titleRowBounds.Y,
            Width = IconSize,
            Height = IconSize,
            Text = headerIconPath,
        };
    }

    private static IRegionLayoutPolicy SelectPolicy(RegionPolicy policy) => policy switch
    {
        RegionPolicy.Absolute => AbsolutePolicy,
        RegionPolicy.RightPacked => RightPackedPolicy,
        RegionPolicy.TopStacked => TopStackedPolicy,
        RegionPolicy.SingleControl => SingleControlPolicy,
        _ => throw new InvalidOperationException($"Unsupported region policy '{policy}'."),
    };

    private static MsiControlModel BuildControlModel(ResolvedControlPlacement entry)
    {
        var source = entry.Source;
        if (!ControlTypeLookup.TryGetValue(source.Type, out var controlType))
        {
            throw new InvalidOperationException(
                $"Unknown MSI control type '{source.Type}' on placed control '{source.Name}'.");
        }

        return new MsiControlModel
        {
            Name = source.Name,
            Type = controlType,
            X = entry.Bounds.X,
            Y = entry.Bounds.Y,
            Width = entry.Bounds.Width,
            Height = entry.Bounds.Height,
            Property = source.Property,
            Text = string.IsNullOrEmpty(source.TextOrLocKey) ? null : source.TextOrLocKey,
        };
    }

    private static string? FindFallbackFirstControl(DialogContent content)
    {
        if (content.Placements.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var placement in content.Placements)
        {
            if (!string.Equals(placement.RegionName, "ButtonRow", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var control in placement.Controls)
            {
                if (string.Equals(control.Type, "PushButton", StringComparison.Ordinal))
                {
                    return control.Name;
                }
            }
        }

        return null;
    }

    private static FrozenDictionary<string, MsiControlType> BuildControlTypeLookup()
    {
        // Member-name spelling matches the exact Control table Type column string for each enum value.
        var entries = new Dictionary<string, MsiControlType>(StringComparer.Ordinal)
        {
            ["Text"] = MsiControlType.Text,
            ["PushButton"] = MsiControlType.PushButton,
            ["Line"] = MsiControlType.Line,
            ["CheckBox"] = MsiControlType.CheckBox,
            ["ScrollableText"] = MsiControlType.ScrollableText,
            ["PathEdit"] = MsiControlType.PathEdit,
            ["SelectionTree"] = MsiControlType.SelectionTree,
            ["VolumeCostList"] = MsiControlType.VolumeCostList,
            ["ProgressBar"] = MsiControlType.ProgressBar,
            ["Bitmap"] = MsiControlType.Bitmap,
            ["RadioButtonGroup"] = MsiControlType.RadioButtonGroup,
            ["ComboBox"] = MsiControlType.ComboBox,
            ["Edit"] = MsiControlType.Edit,
            ["ListBox"] = MsiControlType.ListBox,
            ["DirectoryCombo"] = MsiControlType.DirectoryCombo,
            ["DirectoryList"] = MsiControlType.DirectoryList,
            ["MaskedEdit"] = MsiControlType.MaskedEdit,
            ["Icon"] = MsiControlType.Icon,
            ["GroupBox"] = MsiControlType.GroupBox,
        };

        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
