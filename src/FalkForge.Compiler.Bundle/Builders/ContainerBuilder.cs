namespace FalkForge.Compiler.Bundle.Builders;

public sealed class ContainerBuilder
{
    private string? _downloadUrl;
    private string _id = string.Empty;

    public ContainerBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public ContainerBuilder DownloadUrl(string url)
    {
        _downloadUrl = url;
        return this;
    }

    internal ContainerModel Build()
    {
        return new ContainerModel
        {
            Id = _id,
            DownloadUrl = _downloadUrl
        };
    }
}