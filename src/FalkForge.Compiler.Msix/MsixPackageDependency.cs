namespace FalkForge.Compiler.Msix;

public sealed class MsixPackageDependency
{
    public required string Name { get; init; }
    public required string Publisher { get; init; }
    public Version? MinVersion { get; init; }
}
