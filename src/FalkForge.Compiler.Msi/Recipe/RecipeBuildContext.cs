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

    // Non-fatal diagnostics raised by producers during recipe build. Producers only see this
    // context (not the IFalkLogger passed to MsiRecipeBuilder.Build), so a producer that needs
    // to surface a warning queues it here; MsiRecipeBuilder drains the queue through the logger
    // once the producer pipeline finishes.
    private readonly List<(string Code, string Message)> _warnings = new();

    // Lazily cached filename→component map. Built once on first access and reused
    // by both MsiAssemblyTableProducer and MsiAssemblyNameTableProducer so the
    // O(components × files) scan runs at most once per build.
    private Dictionary<string, ResolvedComponent>? _fileToComponentMap;

    // Lazily cached synthesized Directory-table ids, keyed by (root token, relative
    // path). DirectoryTreeSynthesizer.ComputeDirectoryId re-walks the ancestor chain
    // and re-hashes every segment on each call; multiple files/components that share
    // a directory would otherwise pay that cost once per caller instead of once per
    // build. Scoped to this context (not a global static cache) because the result
    // also depends on the package's configured install directory. That install
    // directory is read internally from Resolved.Package.DefaultInstallDirectory
    // (not accepted per-call), so it is a build constant for the lifetime of this
    // context — that makes (root token, relative path) a complete cache key and
    // removes any chance of a caller passing a mismatched installDir and getting a
    // stale id back.
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

    /// <summary>Non-fatal diagnostics queued by producers via <see cref="AddWarning"/>.</summary>
    internal IReadOnlyList<(string Code, string Message)> Warnings => _warnings;

    /// <summary>Queues a non-fatal diagnostic to be logged as a <c>Warning</c> once the
    /// producer pipeline finishes. <paramref name="code"/> is a stable diagnostic code
    /// (e.g. <c>DLG004</c>) surfaced alongside <paramref name="message"/>.</summary>
    internal void AddWarning(string code, string message) => _warnings.Add((code, message));

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
    /// re-hashing every segment. The install directory that
    /// <see cref="DirectoryTreeSynthesizer.ComputeDirectoryId"/> also depends on is
    /// read here from <see cref="ResolvedPackage.Package"/>'s
    /// <see cref="PackageModel.DefaultInstallDirectory"/> rather than accepted as a
    /// parameter — it is a build constant, so the cache key is inherently complete and
    /// no caller can pass a mismatched value. Delegating to the synthesizer means the
    /// cache changes nothing about the resulting id, only how often it gets recomputed.
    /// </summary>
    internal string GetOrComputeDirectoryId(InstallPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        _directoryIdCache ??= new Dictionary<(string, string), string>();
        (string RootToken, string RelativePath) key = (path.Root.Token, path.RelativePath);
        if (_directoryIdCache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        string computed = DirectoryTreeSynthesizer.ComputeDirectoryId(
            path, Resolved.Package.DefaultInstallDirectory);
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
