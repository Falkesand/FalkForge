namespace FalkForge.Compiler.Msix;

public sealed class MsixApplication
{
    public required string Id { get; init; }
    public required string Executable { get; init; }
    public string? EntryPoint { get; init; }
    public required MsixVisualElements VisualElements { get; init; }
}
