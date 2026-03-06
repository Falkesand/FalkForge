namespace FalkForge.Compiler.Msix;

public sealed class MsixVisualElements
{
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public string BackgroundColor { get; init; } = "#transparent";
    public string? Square150x150Logo { get; init; }
    public string? Square44x44Logo { get; init; }
    public string? Wide310x150Logo { get; init; }
}
