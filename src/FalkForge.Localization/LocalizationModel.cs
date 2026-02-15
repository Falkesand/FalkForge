namespace FalkForge.Localization;

public sealed class LocalizationModel
{
    public required string Culture { get; init; }
    public IReadOnlyDictionary<string, string> Strings { get; init; } = new Dictionary<string, string>();
}
