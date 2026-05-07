using System.Collections.Frozen;
using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Per-validation-run context. Built once by the engine in an O(n) pre-pass.
/// Rules read from it; rules never mutate it. Cross-table lookups become
/// O(1) FrozenDictionary hits; tree-walking rules iterate a pre-flattened list.
/// </summary>
public sealed class RuleContext
{
    /// <summary>The package being validated.</summary>
    public PackageModel Package { get; }

    /// <summary>
    /// Pre-built feature index, keyed by feature ID.
    /// O(1) lookup for cross-table rules checking FeatureComponentRef.
    /// </summary>
    public FrozenDictionary<string, FeatureModel> FeaturesById { get; }

    /// <summary>Pre-built custom table index, keyed by table name.</summary>
    public FrozenDictionary<string, CustomTableModel> CustomTablesByName { get; }

    /// <summary>
    /// Pre-walked feature tree — flat list of (feature, depth, path) tuples
    /// in depth-first order. Any rule that needs to iterate features uses this
    /// instead of recursing to avoid duplicate tree traversal.
    /// </summary>
    public ImmutableArray<FeatureWalkEntry> FeatureWalk { get; }

    internal RuleContext(
        PackageModel package,
        FrozenDictionary<string, FeatureModel> featuresById,
        FrozenDictionary<string, CustomTableModel> customTablesByName,
        ImmutableArray<FeatureWalkEntry> featureWalk)
    {
        Package = package;
        FeaturesById = featuresById;
        CustomTablesByName = customTablesByName;
        FeatureWalk = featureWalk;
    }

    /// <summary>
    /// Factory for tests — builds a fully populated context from any package.
    /// Does not require the full validation engine.
    /// </summary>
    public static RuleContext ForTest(PackageModel package)
        => RuleContextBuilder.Build(package);
}

/// <summary>
/// Entry produced by the depth-first feature tree walk stored in <see cref="RuleContext.FeatureWalk"/>.
/// </summary>
public readonly record struct FeatureWalkEntry(FeatureModel Feature, int Depth, ModelPath Path);
