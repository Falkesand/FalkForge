using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Models;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Feature-tree reconstruction: joins <see cref="FeatureRow"/> parent/child
/// links with <see cref="FeatureComponentsRow"/> component refs into the
/// nested <see cref="FeatureModel"/> hierarchy.
/// </summary>
public static partial class MsiPackageReconstructor
{
    private static List<FeatureModel> BuildFeatures(
        IReadOnlyList<FeatureRow> featureRows,
        IReadOnlyList<FeatureComponentsRow> featureComponentsRows)
    {
        var featureCompMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var fc in featureComponentsRows)
        {
            if (!featureCompMap.TryGetValue(fc.Feature_, out var list))
            {
                list = [];
                featureCompMap[fc.Feature_] = list;
            }
            list.Add(fc.Component_);
        }

        var rawFeatures = featureRows
            .Select(r => new
            {
                r.Feature, ParentId = string.IsNullOrEmpty(r.Feature_Parent) ? null : r.Feature_Parent,
                r.Title, r.Description, r.Display, r.Level, r.Directory_, r.Attributes
            })
            .ToList();

        return BuildTree(rawFeatures, null, featureCompMap);
    }

    /// <summary>msidbFeatureAttributesUIDisallowAbsent — matches <c>FeatureTableProducer</c>'s
    /// <c>FeatureUiDisallowAbsentAttribute</c>, the bit that marks a feature required.</summary>
    private const int FeatureUiDisallowAbsentAttribute = 16;

    /// <summary>Level value <c>FeatureTableProducer</c> writes for an installed (required or
    /// optional-default) feature. Non-installed features get Level=1000; there is no Level=0 case.</summary>
    private const int FeatureLevelInstall = 1;

    private static List<FeatureModel> BuildTree(
        IReadOnlyList<dynamic> all,
        string? parentId,
        Dictionary<string, List<string>> compMap)
    {
        var result = new List<FeatureModel>();
        foreach (var r in all)
        {
            if (r.ParentId != parentId) continue;
            compMap.TryGetValue((string)r.Feature, out var refs);
            result.Add(new FeatureModel
            {
                Id = r.Feature,
                Title = r.Title,
                Description = r.Description,
                IsRequired = ((int)r.Attributes & FeatureUiDisallowAbsentAttribute) != 0,
                IsDefault = r.Level == FeatureLevelInstall,
                DisplayLevel = r.Level,
                Children = BuildTree(all, (string)r.Feature, compMap),
                ComponentRefs = refs ?? []
            });
        }
        return result;
    }
}
