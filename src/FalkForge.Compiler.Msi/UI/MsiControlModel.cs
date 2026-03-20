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
    public string? NextControl { get; init; }
}
