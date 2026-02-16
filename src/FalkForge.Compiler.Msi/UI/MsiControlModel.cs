namespace FalkForge.Compiler.Msi.UI;

internal sealed class MsiControlModel
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Attributes { get; init; } = 3; // Visible | Enabled
    public string? Property { get; init; }
    public string? Text { get; init; }
    public string? NextControl { get; init; }
}
