namespace FalkForge.Models;

/// <summary>
/// WinGet installer type. Controls which InstallerType value is emitted in the manifest.
/// </summary>
public enum WinGetInstallerType
{
    /// <summary>MSI installer (default).</summary>
    Msi,
    /// <summary>EXE installer (e.g., a FalkForge bundle).</summary>
    Exe
}

/// <summary>
/// Additional locale entry for WinGet manifest generation.
/// Produces a separate locale manifest file (e.g., Contoso.App.locale.sv-SE.yaml).
/// </summary>
public sealed class WinGetLocale
{
    /// <summary>BCP 47 locale tag (e.g., "sv-SE").</summary>
    public required string Locale { get; init; }

    /// <summary>Publisher name in this locale.</summary>
    public required string Publisher { get; init; }

    /// <summary>Package name in this locale.</summary>
    public required string PackageName { get; init; }

    /// <summary>Short description in this locale.</summary>
    public required string ShortDescription { get; init; }

    /// <summary>Optional full description in this locale.</summary>
    public string? Description { get; init; }

    /// <summary>Optional license text or SPDX identifier in this locale.</summary>
    public string? License { get; init; }
}

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

    /// <summary>
    /// Installer type emitted in the manifest. Defaults to <see cref="WinGetInstallerType.Msi"/>.
    /// Set to <see cref="WinGetInstallerType.Exe"/> for FalkForge bundles.
    /// </summary>
    public WinGetInstallerType InstallerType { get; init; } = WinGetInstallerType.Msi;

    /// <summary>
    /// Additional locale entries. Each entry produces a separate locale manifest file.
    /// The default en-US locale manifest is always written from the top-level config fields.
    /// </summary>
    public WinGetLocale[]? Locales { get; init; }
}
