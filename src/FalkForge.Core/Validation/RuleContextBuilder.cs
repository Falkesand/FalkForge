using System.Collections.Frozen;
using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Builds a <see cref="RuleContext"/> from a <see cref="PackageModel"/> in a single O(n) pre-pass.
/// Shared indexes amortize cross-table lookup cost across all rules in a run.
/// </summary>
internal static class RuleContextBuilder
{
    public static RuleContext Build(PackageModel package)
    {
        // Walk feature tree once: build FeaturesById index and FeatureWalk list
        var featuresById = new Dictionary<string, FeatureModel>(StringComparer.Ordinal);
        var walkBuilder = ImmutableArray.CreateBuilder<FeatureWalkEntry>();
        WalkFeatures(package.Features, depth: 0, ModelPath.Root.Field("Features"), featuresById, walkBuilder);

        var customTablesByName = package.CustomTables
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .ToFrozenDictionary(t => t.Name, StringComparer.Ordinal);

        return new RuleContext(
            package,
            featuresById.ToFrozenDictionary(StringComparer.Ordinal),
            customTablesByName,
            walkBuilder.ToImmutable());
    }

    private static void WalkFeatures(
        IReadOnlyList<FeatureModel> features,
        int depth,
        ModelPath basePath,
        Dictionary<string, FeatureModel> index,
        ImmutableArray<FeatureWalkEntry>.Builder walk)
    {
        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            var path = basePath.Index(i);
            walk.Add(new FeatureWalkEntry(feature, depth, path));

            if (!string.IsNullOrWhiteSpace(feature.Id))
                index.TryAdd(feature.Id, feature);

            if (feature.Children.Count > 0)
                WalkFeatures(feature.Children, depth + 1, path.Field("Children"), index, walk);
        }
    }
}
