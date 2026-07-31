namespace FalkForge.Compiler.Msi.UI;

internal sealed class MsiControlModel
{
    public required string Name { get; init; }
    public required MsiControlType Type { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public MsiControlAttributes Attributes { get; init; } = MsiControlAttributes.Visible | MsiControlAttributes.Enabled;
    public string? Property { get; init; }
    public string? Text { get; set; }
    // Settable (not init): DialogTabCycle.Assign resolves the Control_Next tab-cycle chain in
    // place once the control list is final, mirroring Text above (localization rewrites it in
    // place for the same reason — the value is not known at construction time).
    public string? NextControl { get; set; }
}
