namespace FalkInstaller.Models;

public sealed class LocalizationData
{
    public required string Culture { get; init; }
    public IReadOnlyDictionary<string, string> Strings { get; init; } = new Dictionary<string, string>();
}
