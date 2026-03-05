namespace FalkForge.Models;

public sealed class PropertyModel
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public bool IsSecure { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsHidden { get; init; }
}