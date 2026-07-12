using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis.Builders;

public sealed class WebSiteBuilder
{
    private readonly List<WebBindingModel> _bindings = [];
    private readonly List<WebApplicationModel> _webApplications = [];
    private readonly List<WebVirtualDirectoryModel> _virtualDirectories = [];
    private string? _appPool;
    private bool _autoStart = true;
    private int _connectionTimeout = 120;
    private string _description = string.Empty;
    private string _directory = string.Empty;
    private string _id = string.Empty;

    public WebSiteBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public WebSiteBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public WebSiteBuilder Directory(string directory)
    {
        _directory = directory;
        return this;
    }

    public WebSiteBuilder Binding(Action<WebBindingBuilder> configure)
    {
        var builder = new WebBindingBuilder();
        configure(builder);
        _bindings.Add(builder.Build());
        return this;
    }

    public WebSiteBuilder Binding(int port, string protocol = "http", string? hostHeader = null)
    {
        _bindings.Add(new WebBindingModel
        {
            Protocol = protocol,
            Port = port,
            HostHeader = hostHeader,
            IpAddress = "*"
        });
        return this;
    }

    public WebSiteBuilder AppPool(string appPool)
    {
        _appPool = appPool;
        return this;
    }

    public WebSiteBuilder AppPool(AppPoolRef appPoolRef)
    {
        return AppPool(appPoolRef.Id);
    }

    public WebSiteBuilder AutoStart(bool autoStart)
    {
        _autoStart = autoStart;
        return this;
    }

    public WebSiteBuilder ConnectionTimeout(int seconds)
    {
        _connectionTimeout = seconds;
        return this;
    }

    public WebSiteBuilder AddApplication(Action<WebApplicationBuilder> configure)
    {
        var builder = new WebApplicationBuilder();
        configure(builder);
        _webApplications.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Authors a virtual directory mounted under this site (by default under its root application,
    /// <c>/</c>). Genuinely created at install via <c>Microsoft.Web.Administration</c> — see
    /// <see cref="WebVirtualDirectoryBuilder"/>.
    /// </summary>
    public WebSiteBuilder VirtualDirectory(Action<WebVirtualDirectoryBuilder> configure)
    {
        var builder = new WebVirtualDirectoryBuilder();
        configure(builder);
        _virtualDirectories.Add(builder.Build());
        return this;
    }

    internal WebSiteModel Build()
    {
        return new WebSiteModel
        {
            Id = string.IsNullOrEmpty(_id) ? _description : _id,
            Description = _description,
            Directory = _directory,
            Bindings = _bindings,
            AppPool = _appPool,
            AutoStart = _autoStart,
            ConnectionTimeout = _connectionTimeout,
            WebApplications = _webApplications,
            VirtualDirectories = _virtualDirectories
        };
    }
}