using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class DialogDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = "[ProductName] Setup";

    [JsonPropertyName("width")]
    public int Width { get; set; } = 370;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 270;

    [JsonPropertyName("controls")]
    public List<DialogControlDefinition> Controls { get; set; } = [];
}
