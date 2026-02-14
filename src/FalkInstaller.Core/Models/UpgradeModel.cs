namespace FalkInstaller.Models;

public sealed class UpgradeModel
{
    public bool AllowDowngrades { get; init; }
    public bool AllowSameVersion { get; init; }
    public string? MinimumVersion { get; init; }
    public string? MaximumVersion { get; init; }
    public string? DowngradeErrorMessage { get; init; } = "A newer version is already installed.";
}
