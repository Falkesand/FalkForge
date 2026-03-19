namespace FalkForge.Compiler.Msix;

public sealed class MsixExtension
{
    public required string Category { get; init; }
    public string? EntryPoint { get; init; }
}
