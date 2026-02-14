namespace FalkInstaller.Compiler.Bundle.Compilation;

public sealed class PayloadEntry
{
    public required string PackageId { get; init; }
    public required byte[] Data { get; init; }
    public required string Sha256Hash { get; init; }
}
