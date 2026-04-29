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
/// and applies four customization verbs to the produced model: <see cref="DialogCustomizationModel.WindowTitle"/>
/// overrides <see cref="MsiDialogModel.Title"/>, <see cref="DialogCustomizationModel.BannerBitmap"/>
/// rewrites the <c>Text</c> of every <c>Bitmap</c>-typed control, <see cref="DialogCustomizationModel.HeaderIcon"/>
/// rewrites the <c>Text</c> of every <c>Icon</c>-typed control, and entries in
/// <see cref="DialogCustomizationModel.ButtonLabelOverrides"/> rewrite the <c>Text</c> of
/// the matching <c>PushButton</c> identified through <see cref="DialogButtonNames.Map"/>.
/// Suppression of stock dialogs (<see cref="DialogCustomizationModel.SuppressedDialogs"/>) is
/// not applied here — that is a dialog-set-level concern handled by the emitter that decides
/// which dialogs to compose at all.
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

        ApplyCustomization(model, customization);

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

    private static void ApplyCustomization(MsiDialogModel model, DialogCustomizationModel? customization)
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
        var headerIcon = customization.HeaderIcon;
        var hasBanner = !string.IsNullOrEmpty(bannerBitmap);
        var hasIcon = !string.IsNullOrEmpty(headerIcon);
        var hasButtonOverrides = buttonOverrides is { Count: > 0 };

        if (!hasBanner && !hasIcon && !hasButtonOverrides)
        {
            return;
        }

        foreach (var control in model.Controls)
        {
            if (hasBanner && control.Type == MsiControlType.Bitmap)
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
