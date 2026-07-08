using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Models;
using FalkForge.Compiler.Msi.Recipe.Producers;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Mutable container threaded through the recipe-build pipeline. Producers read
/// the resolved package and previously built tables from this context, then
/// publish their own table via <see cref="AddBuiltTable"/>. The <c>BuiltTables</c>
/// view is frozen on every read so producers cannot mutate prior table contents.
/// Not a record — identity matters because producers append to it across the
/// pipeline.
/// </summary>
internal sealed class RecipeBuildContext
{
    private readonly Dictionary<TableId, ImmutableArray<RecipeRow>> _builtTables = new();

    // Lazily cached filename→component map. Built once on first access and reused
    // by both MsiAssemblyTableProducer and MsiAssemblyNameTableProducer so the
    // O(components × files) scan runs at most once per build.
    private Dictionary<string, ResolvedComponent>? _fileToComponentMap;

    // Lazily cached synthesized Directory-table ids, keyed by (root token, relative
    // path). DirectoryTreeSynthesizer.ComputeDirectoryId re-walks the ancestor chain
    // and re-hashes every segment on each call; multiple files/components that share
    // a directory would otherwise pay that cost once per caller instead of once per
    // build. Scoped to this context (not a global static cache) because the result
    // also depends on the package's configured install directory, which is fixed for
    // the lifetime of one RecipeBuildContext but can differ across builds.
    private Dictionary<(string RootToken, string RelativePath), string>? _directoryIdCache;

    public RecipeBuildContext(
        ResolvedPackage resolved,
        MsiRecipeBuildOptions options,
        IFileSequencer fileSequencer,
        IStreamRegistry streams)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSequencer);
        ArgumentNullException.ThrowIfNull(streams);

        Resolved = resolved;
        Options = options;
        FileSequencer = fileSequencer;
        Streams = streams;
    }

    /// <summary>The resolved package input to the recipe build.</summary>
    public ResolvedPackage Resolved { get; }

    /// <summary>Build options controlling sequencing, hashing, and memory thresholds.</summary>
    public MsiRecipeBuildOptions Options { get; }

    /// <summary>Strategy used to compute file sequence numbers.</summary>
    public IFileSequencer FileSequencer { get; }

    /// <summary>Stream payload registry shared across all producers.</summary>
    public IStreamRegistry Streams { get; }

    /// <summary>Frozen view over all tables built so far. Producers may read but not mutate.</summary>
    public IReadOnlyDictionary<TableId, ImmutableArray<RecipeRow>> BuiltTables
        => _builtTables.ToFrozenDictionary();

    /// <summary>
    /// Returns the filename-to-component lookup map, building and caching it on
    /// first access. The map is case-insensitive and uses first-match-wins semantics
    /// (same convention as legacy <c>EmitAssemblies</c>). Multiple producers that
    /// need the same lookup share the single instance built here rather than each
    /// constructing their own O(components × files) dictionary.
    /// </summary>
    internal Dictionary<string, ResolvedComponent> GetOrBuildFileToComponentMap()
        => _fileToComponentMap ??=
            ProducerHelpers.BuildFileToComponentMap(Resolved.Components);

    /// <summary>
    /// Returns the synthesized Directory table id for <paramref name="path"/>, caching
    /// by (root token, relative path) so repeated lookups for a shared directory (e.g.
    /// many files under the same folder) skip re-walking the ancestor chain and
    /// re-hashing every segment. Delegates the actual computation to
    /// <see cref="DirectoryTreeSynthesizer.ComputeDirectoryId"/> — the cache changes
    /// nothing about the resulting id, only how often it gets recomputed.
    /// </summary>
    internal string GetOrComputeDirectoryId(InstallPath path, InstallPath? installDir)
    {
        ArgumentNullException.ThrowIfNull(path);

        _directoryIdCache ??= new Dictionary<(string, string), string>();
        (string RootToken, string RelativePath) key = (path.Root.Token, path.RelativePath);
        if (_directoryIdCache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        string computed = DirectoryTreeSynthesizer.ComputeDirectoryId(path, installDir);
        _directoryIdCache[key] = computed;
        return computed;
    }

    /// <summary>
    /// Append a producer's table output to the build state. Throws
    /// <see cref="InvalidOperationException"/> on duplicate table id —
    /// duplicate publication indicates a producer-pipeline misconfiguration.
    /// </summary>
    internal void AddBuiltTable(TableId id, ImmutableArray<RecipeRow> rows)
    {
        if (!_builtTables.TryAdd(id, rows))
        {
            throw new InvalidOperationException(
                $"Table '{id.Value}' has already been built.");
        }
    }
}
