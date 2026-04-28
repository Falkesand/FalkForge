namespace FalkForge.Compiler.Msi.UI;

/// <summary>
/// Represents an EventMapping table row that subscribes a dialog control to an MSI event.
/// The MSI engine fires events (e.g. SetProgress, ActionText) during installation and
/// routes them to controls that have matching subscriptions.
/// </summary>
internal sealed class MsiEventMappingModel
{
    public required string DialogName { get; init; }
    public required string ControlName { get; init; }

    /// <summary>MSI event name, e.g. "SetProgress", "ActionText", "ActionData".</summary>
    public required string Event { get; init; }

    /// <summary>Control attribute to update when the event fires, e.g. "Progress", "Text".</summary>
    public required string Attribute { get; init; }
}
