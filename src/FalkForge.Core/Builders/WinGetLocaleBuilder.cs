using FalkForge.Models;

namespace FalkForge.Builders;

/// <summary>
/// Fluent builder for an additional WinGet locale entry. Produces a separate locale manifest
/// file (e.g. Contoso.App.locale.sv-SE.yaml) alongside the default en-US locale manifest.
/// </summary>
public sealed class WinGetLocaleBuilder
{
    private string? _locale;
    private string? _publisher;
    private string? _packageName;
    private string? _shortDescription;
    private string? _description;
    private string? _license;

    public WinGetLocaleBuilder Locale(string locale)
    {
        _locale = locale;
        return this;
    }

    public WinGetLocaleBuilder Publisher(string publisher)
    {
        _publisher = publisher;
        return this;
    }

    public WinGetLocaleBuilder PackageName(string packageName)
    {
        _packageName = packageName;
        return this;
    }

    public WinGetLocaleBuilder ShortDescription(string desc)
    {
        _shortDescription = desc;
        return this;
    }

    public WinGetLocaleBuilder Description(string desc)
    {
        _description = desc;
        return this;
    }

    public WinGetLocaleBuilder License(string license)
    {
        _license = license;
        return this;
    }

    internal WinGetLocale? Build()
    {
        if (_locale is null || _publisher is null || _packageName is null || _shortDescription is null)
            return null;

        return new WinGetLocale
        {
            Locale = _locale,
            Publisher = _publisher,
            PackageName = _packageName,
            ShortDescription = _shortDescription,
            Description = _description,
            License = _license
        };
    }
}
