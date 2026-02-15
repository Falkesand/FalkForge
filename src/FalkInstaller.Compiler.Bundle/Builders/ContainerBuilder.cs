namespace FalkInstaller.Compiler.Bundle.Builders;

public sealed class ContainerBuilder
{
    private string _id = string.Empty;
    private string? _downloadUrl;

    public ContainerBuilder Id(string id) { _id = id; return this; }
    public ContainerBuilder DownloadUrl(string url) { _downloadUrl = url; return this; }

    internal ContainerModel Build()
    {
        return new ContainerModel
        {
            Id = _id,
            DownloadUrl = _downloadUrl
        };
    }
}
