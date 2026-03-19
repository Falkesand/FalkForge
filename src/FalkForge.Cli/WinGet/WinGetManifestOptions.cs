namespace FalkForge.Cli.WinGet;

public sealed class WinGetManifestOptions
{
    public required string PackageIdentifier { get; init; }
    public required string PackageVersion { get; init; }
    public required string Publisher { get; init; }
    public required string PackageName { get; init; }
    public required string ShortDescription { get; init; }
    public required string InstallerUrl { get; init; }
    public required string InstallerSha256 { get; init; }
    public string InstallerType { get; init; } = "msi";
    public string Architecture { get; init; } = "x64";
    public string PackageLocale { get; init; } = "en-US";
    public string License { get; init; } = "Proprietary";
}
