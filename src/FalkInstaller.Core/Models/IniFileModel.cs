namespace FalkInstaller.Models;

public sealed class IniFileModel
{
    public required string FileName { get; init; }
    public string? DirProperty { get; init; }
    public required string Section { get; init; }
    public required string Key { get; init; }
    public required string Value { get; init; }
    public IniFileAction Action { get; init; } = IniFileAction.CreateEntry;
    public string? FeatureRef { get; init; }
}
