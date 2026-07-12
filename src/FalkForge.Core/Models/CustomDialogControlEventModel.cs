namespace FalkForge.Models;

/// <summary>
/// A single <c>ControlEvent</c> table row fired by an authored control: the MSI event verb,
/// its argument, an optional gating condition, and an ordering for rows that share a control
/// and verb. The owning control name and dialog name are supplied by the enclosing
/// <see cref="CustomDialogControlModel"/> / <see cref="CustomDialogModel"/> at compile time.
/// </summary>
public sealed record CustomDialogControlEventModel
{
    /// <summary>
    /// The MSI event verb. Standard values: <c>NewDialog</c>, <c>SpawnDialog</c>,
    /// <c>EndDialog</c>, <c>DoAction</c>, <c>Reset</c>, <c>AddLocal</c>, <c>Remove</c>;
    /// the <c>[PropertyName]</c> form assigns <see cref="Argument"/> to that MSI property.
    /// </summary>
    public required string Event { get; init; }

    /// <summary>
    /// The event-specific argument: a target dialog name (<c>NewDialog</c>/<c>SpawnDialog</c>),
    /// an exit code (<c>EndDialog</c>: <c>Return</c>, <c>Exit</c>, <c>Retry</c>, <c>Ignore</c>),
    /// a custom-action name (<c>DoAction</c>), or a property value. Empty when unused.
    /// </summary>
    public string Argument { get; init; } = string.Empty;

    /// <summary>
    /// The MSI condition that gates the event. <see langword="null"/> means "always fire"
    /// (the compiler emits the canonical <c>"1"</c> condition).
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// Fire order when multiple events share the same control and verb. Lower fires first.
    /// Defaults to 1.
    /// </summary>
    public int Ordering { get; init; } = 1;
}
