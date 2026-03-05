using FalkForge.Models;

namespace FalkForge.Extensibility;

public sealed class ExtensionContext
{
    public required PackageModel Package { get; init; }
    public required string OutputDirectory { get; init; }
    public required string SourceDirectory { get; init; }
}