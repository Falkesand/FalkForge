using FalkInstaller.Extensions.Iis.Models;

namespace FalkInstaller.Extensions.Iis.Builders;

public sealed class WebApplicationBuilder
{
    private string _id = string.Empty;
    private string _alias = string.Empty;
    private string _directory = string.Empty;
    private string? _appPool;

    public WebApplicationBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public WebApplicationBuilder Alias(string alias)
    {
        _alias = alias;
        return this;
    }

    public WebApplicationBuilder Directory(string directory)
    {
        _directory = directory;
        return this;
    }

    public WebApplicationBuilder AppPool(string appPool)
    {
        _appPool = appPool;
        return this;
    }

    internal WebApplicationModel Build() => new()
    {
        Id = string.IsNullOrEmpty(_id) ? _alias : _id,
        Alias = _alias,
        Directory = _directory,
        AppPool = _appPool
    };
}
