using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class DialogControlDefinition
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DialogControlType Type { get; set; } = DialogControlType.Text;

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; } = 56;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 17;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("property")]
    public string? Property { get; set; }

    [JsonPropertyName("condition")]
    public string? Condition { get; set; }
}
