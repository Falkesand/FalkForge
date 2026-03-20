namespace FalkForge.Models;

/// <summary>
/// Configuration for WinGet manifest generation.
/// Stores user-provided metadata that cannot be derived from the MSI package.
/// </summary>
public sealed class WinGetConfig
{
    /// <summary>
    /// WinGet package identifier in Publisher.PackageName format (e.g., "Contoso.MyApp").
    /// </summary>
    public required string PackageIdentifier { get; init; }

    /// <summary>
    /// Download URL for the installer. If null, a placeholder comment is emitted.
    /// </summary>
    public string? InstallerUrl { get; init; }

    /// <summary>
    /// SPDX license identifier or license text (e.g., "MIT", "Apache-2.0").
    /// </summary>
    public required string License { get; init; }

    /// <summary>
    /// Brief description of the package (max 256 characters).
    /// </summary>
    public required string ShortDescription { get; init; }

    /// <summary>
    /// Optional short name for the package used in winget install.
    /// </summary>
    public string? Moniker { get; init; }

    /// <summary>
    /// Optional tags for package discovery.
    /// </summary>
    public string[]? Tags { get; init; }

    /// <summary>
    /// Optional release notes for this version.
    /// </summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>
    /// Optional URL to the release notes.
    /// </summary>
    public string? ReleaseNotesUrl { get; init; }

    /// <summary>
    /// Optional privacy policy URL.
    /// </summary>
    public string? PrivacyUrl { get; init; }

    /// <summary>
    /// WinGet manifest schema version. Defaults to "1.9.0".
    /// </summary>
    public string ManifestVersion { get; init; } = "1.9.0";
}
