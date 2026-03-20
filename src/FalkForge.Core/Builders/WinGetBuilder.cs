using FalkForge.Models;

namespace FalkForge.Builders;

/// <summary>
/// Fluent builder for WinGet manifest configuration.
/// </summary>
public sealed class WinGetBuilder
{
    private string? _packageIdentifier;
    private string? _installerUrl;
    private string? _license;
    private string? _shortDescription;
    private string? _moniker;
    private string[]? _tags;
    private string? _releaseNotes;
    private string? _releaseNotesUrl;
    private string? _privacyUrl;
    private string _manifestVersion = "1.9.0";

    public WinGetBuilder PackageIdentifier(string id)
    {
        _packageIdentifier = id;
        return this;
    }

    public WinGetBuilder InstallerUrl(string url)
    {
        _installerUrl = url;
        return this;
    }

    public WinGetBuilder License(string license)
    {
        _license = license;
        return this;
    }

    public WinGetBuilder ShortDescription(string desc)
    {
        _shortDescription = desc;
        return this;
    }

    public WinGetBuilder Moniker(string moniker)
    {
        _moniker = moniker;
        return this;
    }

    public WinGetBuilder Tags(params string[] tags)
    {
        _tags = tags;
        return this;
    }

    public WinGetBuilder ReleaseNotes(string notes)
    {
        _releaseNotes = notes;
        return this;
    }

    public WinGetBuilder ReleaseNotesUrl(string url)
    {
        _releaseNotesUrl = url;
        return this;
    }

    public WinGetBuilder PrivacyUrl(string url)
    {
        _privacyUrl = url;
        return this;
    }

    public WinGetBuilder ManifestVersion(string version)
    {
        _manifestVersion = version;
        return this;
    }

    internal WinGetConfig? Build()
    {
        if (_packageIdentifier is null || _license is null || _shortDescription is null)
            return null;

        return new WinGetConfig
        {
            PackageIdentifier = _packageIdentifier,
            InstallerUrl = _installerUrl,
            License = _license,
            ShortDescription = _shortDescription,
            Moniker = _moniker,
            Tags = _tags,
            ReleaseNotes = _releaseNotes,
            ReleaseNotesUrl = _releaseNotesUrl,
            PrivacyUrl = _privacyUrl,
            ManifestVersion = _manifestVersion
        };
    }
}
