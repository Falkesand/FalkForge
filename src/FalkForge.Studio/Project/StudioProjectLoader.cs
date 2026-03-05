using System.IO;
using System.Text.Json;

namespace FalkForge.Studio.Project;

public static class StudioProjectLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static StudioProject NewProject()
    {
        return new StudioProject
        {
            Product = new ProductSection
            {
                UpgradeCode = Guid.NewGuid().ToString()
            },
            Features =
            [
                new FeatureSection { Id = "Main", Title = "Main Application", IsDefault = true, IsRequired = true }
            ]
        };
    }

    public static StudioProject LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<StudioProject>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize project file: {filePath}");
    }

    public static void SaveToFile(StudioProject project, string filePath)
    {
        var json = JsonSerializer.Serialize(project, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public static string Serialize(StudioProject project)
        => JsonSerializer.Serialize(project, JsonOptions);

    public static StudioProject Deserialize(string json)
        => JsonSerializer.Deserialize<StudioProject>(json, JsonOptions)
           ?? throw new InvalidOperationException("Failed to deserialize project JSON.");
}
