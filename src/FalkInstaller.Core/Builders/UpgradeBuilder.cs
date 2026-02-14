namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class UpgradeBuilder
{
    public bool AllowDowngrades { get; set; }
    public bool AllowSameVersion { get; set; }
    public string? MinimumVersion { get; set; }
    public string? MaximumVersion { get; set; }
    public string? DowngradeErrorMessage { get; set; } = "A newer version is already installed.";

    internal UpgradeModel Build() => new()
    {
        AllowDowngrades = AllowDowngrades,
        AllowSameVersion = AllowSameVersion,
        MinimumVersion = MinimumVersion,
        MaximumVersion = MaximumVersion,
        DowngradeErrorMessage = DowngradeErrorMessage
    };
}
