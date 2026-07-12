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
    private WinGetInstallerType _installerType = WinGetInstallerType.Msi;
    private readonly List<WinGetLocale> _locales = [];

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

    /// <summary>
    /// Sets the installer type emitted in the manifest. Defaults to
    /// <see cref="WinGetInstallerType.Msi"/>; use <see cref="WinGetInstallerType.Exe"/> for
    /// FalkForge bundles.
    /// </summary>
    public WinGetBuilder InstallerType(WinGetInstallerType type)
    {
        _installerType = type;
        return this;
    }

    /// <summary>
    /// Adds an additional locale entry, producing a separate locale manifest file
    /// (e.g. Contoso.App.locale.sv-SE.yaml) alongside the default en-US locale manifest.
    /// </summary>
    public WinGetBuilder Locale(Action<WinGetLocaleBuilder> configure)
    {
        var builder = new WinGetLocaleBuilder();
        configure(builder);
        WinGetLocale? locale = builder.Build();
        if (locale is not null)
            _locales.Add(locale);
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
            ManifestVersion = _manifestVersion,
            InstallerType = _installerType,
            Locales = _locales.Count > 0 ? _locales.ToArray() : null
        };
    }
}
