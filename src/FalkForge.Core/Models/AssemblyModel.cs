namespace FalkForge.Models;

public sealed class AssemblyModel
{
    public required string FileRef { get; init; }
    public AssemblyType Type { get; init; } = AssemblyType.DotNetAssembly;
    public string? ApplicationFileRef { get; init; }
    public string? AssemblyName { get; init; }
    public string? AssemblyVersion { get; init; }
    public string? AssemblyCulture { get; init; }
    public string? AssemblyPublicKeyToken { get; init; }
    public string? ProcessorArchitecture { get; init; }
}
