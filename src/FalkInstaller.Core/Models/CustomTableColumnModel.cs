namespace FalkInstaller.Models;

public sealed class CustomTableColumnModel
{
    public required string Name { get; init; }
    public CustomTableColumnType Type { get; init; } = CustomTableColumnType.String;
    public bool PrimaryKey { get; init; }
    public bool Nullable { get; init; }
    public int Width { get; init; } = 255;
    public string? LocalizedDescription { get; init; }
}
