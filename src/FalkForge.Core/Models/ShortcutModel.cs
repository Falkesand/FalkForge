namespace FalkForge.Models;

public sealed class ShortcutModel
{
    public required string Name { get; init; }
    public required string TargetFile { get; init; }
    public IReadOnlyList<ShortcutLocation> Locations { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public string? Arguments { get; init; }
    public string? Description { get; init; }
    public string? IconFile { get; init; }
    public int IconIndex { get; init; }
    public string? StartMenuSubfolder { get; init; }
    public string? FeatureRef { get; init; }
}
