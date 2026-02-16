namespace FalkForge.Extensions.Util.InternetShortcut;

public sealed class InternetShortcutModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Target { get; init; }
    public required string Directory { get; init; }
    public string? IconFile { get; init; }
    public int IconIndex { get; init; }
}
