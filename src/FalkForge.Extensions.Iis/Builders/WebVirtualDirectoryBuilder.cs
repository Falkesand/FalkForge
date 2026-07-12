using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis.Builders;

/// <summary>
/// Fluent authoring for a virtual directory mounted under a <see cref="WebSiteBuilder"/> (via
/// <see cref="WebSiteBuilder.VirtualDirectory(Action{WebVirtualDirectoryBuilder})"/>). Populates
/// <see cref="WebVirtualDirectoryModel"/>, which the IIS execution seam (<c>IisCommandFactory</c>)
/// turns into a genuine <c>Microsoft.Web.Administration</c> virtual directory created at install.
/// </summary>
public sealed class WebVirtualDirectoryBuilder
{
    private string _alias = string.Empty;
    private string _directory = string.Empty;
    private string _id = string.Empty;
    private string? _webApplication;

    public WebVirtualDirectoryBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    /// <summary>The virtual directory's mount path under its parent application, e.g. <c>/reports</c>.</summary>
    public WebVirtualDirectoryBuilder Alias(string alias)
    {
        _alias = alias;
        return this;
    }

    /// <summary>The physical filesystem path the virtual directory points at.</summary>
    public WebVirtualDirectoryBuilder Directory(string directory)
    {
        _directory = directory;
        return this;
    }

    /// <summary>
    /// The parent application's alias (e.g. <c>/api</c>) this virtual directory is mounted under.
    /// Defaults to the site's root application (<c>/</c>) when not set — the only application
    /// guaranteed to exist at install time, since sub-application creation is not yet wired (IIS014).
    /// </summary>
    public WebVirtualDirectoryBuilder WebApplication(string webApplication)
    {
        _webApplication = webApplication;
        return this;
    }

    internal WebVirtualDirectoryModel Build()
    {
        return new WebVirtualDirectoryModel
        {
            Id = string.IsNullOrEmpty(_id) ? _alias : _id,
            Alias = _alias,
            Directory = _directory,
            WebApplication = _webApplication,
        };
    }
}
