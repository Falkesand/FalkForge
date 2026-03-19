namespace FalkForge.Compiler.Msi.UI;

internal sealed class MsiDialogModel
{
    public required string Name { get; init; }
    public string? Title { get; init; }
    public int Width { get; init; } = 370;
    public int Height { get; init; } = 270;
    public int HCentering { get; init; } = 50;
    public int VCentering { get; init; } = 50;
    public int Attributes { get; init; } = 39; // Visible | Modal | Minimize
    public required string FirstControl { get; init; }
    public string? DefaultControl { get; init; }
    public string? CancelControl { get; init; }
    public List<MsiControlModel> Controls { get; init; } = [];
    public List<MsiControlEventModel> Events { get; init; } = [];
    public List<MsiControlConditionModel> Conditions { get; init; } = [];
}