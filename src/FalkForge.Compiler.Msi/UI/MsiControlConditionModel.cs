namespace FalkForge.Compiler.Msi.UI;

internal sealed class MsiControlConditionModel
{
    public required string DialogName { get; init; }
    public required string ControlName { get; init; }
    public required string Action { get; init; }
    public required string Condition { get; init; }
}