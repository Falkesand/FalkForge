namespace FalkForge.Compiler.Msi.UI;

internal sealed class MsiControlEventModel
{
    public required string DialogName { get; init; }
    public required string ControlName { get; init; }
    public required string Event { get; init; }
    public required string Argument { get; init; }
    public string? Condition { get; init; }
    public int Ordering { get; init; }
}
