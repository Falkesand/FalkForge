using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// A coordinate-free description of a dialog: which controls go in which named region.
/// </summary>
/// <remarks>
/// <see cref="DialogContent"/> is the input to <see cref="DialogComposer.Compose"/>; the
/// composer pairs the content with a <see cref="DialogLayout"/> to produce a fully-laid-out
/// <see cref="MsiDialogModel"/>. Events and conditions are intentionally deferred to phase 5+
/// to keep the phase-3 surface minimal.
/// </remarks>
public sealed record DialogContent
{
    private readonly string name = string.Empty;
    private readonly string kind = string.Empty;

    /// <summary>Dialog identifier; must match the C-style identifier pattern.</summary>
    public required string Name
    {
        get => this.name;
        init
        {
            DialogRegion.ValidateIdentifier(value, nameof(Name));
            this.name = value;
        }
    }

    /// <summary>Semantic kind ("Welcome", "License", "InstallDir", etc.). Must match the identifier pattern.</summary>
    public required string Kind
    {
        get => this.kind;
        init
        {
            DialogRegion.ValidateIdentifier(value, nameof(Kind));
            this.kind = value;
        }
    }

    /// <summary>Region placements making up this dialog. May be empty for the skeleton.</summary>
    public required ImmutableArray<RegionPlacement> Placements { get; init; }

    /// <summary>First control to receive focus. Defaults to the first PushButton in ButtonRow if null.</summary>
    public string? FirstControl { get; init; }

    /// <summary>Default control activated by Enter; null defers to the layout policy.</summary>
    public string? DefaultControl { get; init; }

    /// <summary>Cancel control activated by Escape; null defers to the layout policy.</summary>
    public string? CancelControl { get; init; }

    /// <summary>Localization key for the dialog title bar; null leaves the title empty.</summary>
    public string? TitleLocKey { get; init; }

    /// <summary>
    /// Declarative ControlEvent rows fired from this dialog. The composer translates
    /// each entry to an internal <c>MsiControlEventModel</c> on the produced
    /// <see cref="MsiDialogModel"/>. Empty by default.
    /// </summary>
    public ImmutableArray<DialogControlEvent> Events { get; init; } = ImmutableArray<DialogControlEvent>.Empty;

    /// <summary>
    /// Declarative ControlCondition rows that toggle control state via MSI conditions.
    /// Empty by default.
    /// </summary>
    public ImmutableArray<DialogControlCondition> Conditions { get; init; } = ImmutableArray<DialogControlCondition>.Empty;

    /// <summary>
    /// Declarative EventMapping rows subscribing controls to runtime MSI events
    /// such as <c>SetProgress</c> or <c>ActionText</c>. Empty by default.
    /// </summary>
    public ImmutableArray<DialogEventMapping> EventMappings { get; init; } = ImmutableArray<DialogEventMapping>.Empty;
}
