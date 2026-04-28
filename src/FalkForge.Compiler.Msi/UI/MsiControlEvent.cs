namespace FalkForge.Compiler.Msi.UI;

/// <summary>
/// Represents an MSI ControlEvent table Event value. Standard events are exposed
/// as static fields. Property-setting events (<c>[PropertyName]</c>) are created
/// via <see cref="SetProperty"/>.
/// </summary>
internal readonly record struct MsiControlEvent
{
    /// <summary>The raw string written to the MSI ControlEvent table.</summary>
    internal string Value { get; }

    private MsiControlEvent(string value) => Value = value;

    // Standard ControlEvent names
    internal static readonly MsiControlEvent NewDialog = new("NewDialog");
    internal static readonly MsiControlEvent SpawnDialog = new("SpawnDialog");
    internal static readonly MsiControlEvent EndDialog = new("EndDialog");
    internal static readonly MsiControlEvent DoAction = new("DoAction");
    internal static readonly MsiControlEvent AddLocal = new("AddLocal");
    internal static readonly MsiControlEvent AddSource = new("AddSource");
    internal static readonly MsiControlEvent Remove = new("Remove");
    internal static readonly MsiControlEvent Reset = new("Reset");
    internal static readonly MsiControlEvent SelectionBrowse = new("SelectionBrowse");
    internal static readonly MsiControlEvent DirectoryListUp = new("DirectoryListUp");
    internal static readonly MsiControlEvent DirectoryListNew = new("DirectoryListNew");
    internal static readonly MsiControlEvent DirectoryListOpen = new("DirectoryListOpen");

    /// <summary>
    /// Creates a property-setting event that writes a value to an MSI property.
    /// The resulting event string is <c>[PropertyName]</c>.
    /// </summary>
    internal static MsiControlEvent SetProperty(string propertyName) => new($"[{propertyName}]");

    public override string ToString() => Value;
}
