using FalkInstaller.Models;

namespace FalkInstaller.Decompiler.TableReaders;

/// <summary>
/// Reads the Feature table from an MSI database.
/// Columns: Feature, Feature_Parent, Title, Description, Display, Level, Directory_, Attributes
/// </summary>
public static class FeatureTableReader
{
    private static readonly string[] Columns = ["Feature", "Feature_Parent", "Title", "Description", "Display", "Level", "Directory_", "Attributes"];

    private sealed class RawFeature
    {
        public required string Id { get; init; }
        public string? ParentId { get; init; }
        public required string Title { get; init; }
        public string? Description { get; init; }
        public int Display { get; init; }
        public int Level { get; init; }
        public string? DirectoryId { get; init; }
        public int Attributes { get; init; }
    }

    public static Result<List<FeatureModel>> Read(IMsiTableAccess tableAccess)
    {
        var existsResult = tableAccess.TableExists("Feature");
        if (existsResult.IsFailure)
            return Result<List<FeatureModel>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<List<FeatureModel>>.Success([]);

        var rowsResult = tableAccess.QueryTable("Feature", Columns);
        if (rowsResult.IsFailure)
            return Result<List<FeatureModel>>.Failure(ErrorKind.Validation, $"DEC003: Failed to read Feature table. {rowsResult.Error.Message}");

        var rawFeatures = new List<RawFeature>();
        foreach (var row in rowsResult.Value)
        {
            _ = int.TryParse(row[4], out var display);
            _ = int.TryParse(row[5], out var level);
            _ = int.TryParse(row[7], out var attributes);

            rawFeatures.Add(new RawFeature
            {
                Id = row[0] ?? string.Empty,
                ParentId = string.IsNullOrEmpty(row[1]) ? null : row[1],
                Title = row[2] ?? row[0] ?? string.Empty,
                Description = row[3],
                Display = display,
                Level = level,
                DirectoryId = row[6],
                Attributes = attributes
            });
        }

        // Read FeatureComponents table for component references
        var componentRefs = ReadFeatureComponents(tableAccess);

        // Build tree: top-level features have no parent
        return BuildFeatureTree(rawFeatures, null, componentRefs);
    }

    private static Dictionary<string, List<string>> ReadFeatureComponents(IMsiTableAccess tableAccess)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var existsResult = tableAccess.TableExists("FeatureComponents");
        if (existsResult.IsFailure || !existsResult.Value)
            return result;

        var rowsResult = tableAccess.QueryTable("FeatureComponents", ["Feature_", "Component_"]);
        if (rowsResult.IsFailure)
            return result;

        foreach (var row in rowsResult.Value)
        {
            var featureId = row[0] ?? string.Empty;
            var componentId = row[1] ?? string.Empty;

            if (!result.TryGetValue(featureId, out var list))
            {
                list = [];
                result[featureId] = list;
            }
            list.Add(componentId);
        }

        return result;
    }

    private static Result<List<FeatureModel>> BuildFeatureTree(
        List<RawFeature> allFeatures,
        string? parentId,
        Dictionary<string, List<string>> componentRefs)
    {
        var features = new List<FeatureModel>();

        foreach (var raw in allFeatures.Where(f => f.ParentId == parentId))
        {
            var childrenResult = BuildFeatureTree(allFeatures, raw.Id, componentRefs);
            if (childrenResult.IsFailure)
                return childrenResult;

            componentRefs.TryGetValue(raw.Id, out var refs);

            features.Add(new FeatureModel
            {
                Id = raw.Id,
                Title = raw.Title,
                Description = raw.Description,
                IsRequired = raw.Level == 0,
                IsDefault = raw.Level >= 1,
                DisplayLevel = raw.Level,
                Children = childrenResult.Value,
                ComponentRefs = refs ?? []
            });
        }

        return features;
    }
}
