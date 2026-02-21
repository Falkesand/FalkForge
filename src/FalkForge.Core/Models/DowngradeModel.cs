namespace FalkForge.Models;

public sealed class DowngradeModel
{
    public bool AllowDowngrades { get; init; }
    public string? ErrorMessage { get; init; }
}
