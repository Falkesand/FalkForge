using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Immutable, platform-free description of everything that will be written
/// to an MSI database. Produced by the recipe builder from a resolved
/// package; consumed by the executor that applies it to a real
/// <c>msi.dll</c> database. Tests assert against this type directly,
/// bypassing <c>msi.dll</c> entirely.
/// </summary>
public sealed record MsiDatabaseRecipe
{
    public required ImmutableArray<RecipeTable> Tables { get; init; }
    public required SummaryInfoRecipe SummaryInfo { get; init; }
    public required ImmutableDictionary<string, StreamSource> Streams { get; init; }
    public required ImmutableArray<FileSequenceEntry> FileSequencing { get; init; }
    public ImmutableArray<CabinetEmbedding> CabinetEmbeddings { get; init; }
    public required ReadOnlyMemory<byte> ContentHash { get; init; }
}
