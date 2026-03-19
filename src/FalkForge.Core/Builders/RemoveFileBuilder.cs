using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class RemoveFileBuilder
{
    private string? _componentRef;
    private string _directory = string.Empty;
    private string? _fileName;
    private string _id = string.Empty;
    private bool _onInstall;
    private bool _onUninstall;

    public RemoveFileBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public RemoveFileBuilder Directory(string directory)
    {
        _directory = directory;
        return this;
    }

    public RemoveFileBuilder FileName(string fileName)
    {
        _fileName = fileName;
        return this;
    }

    public RemoveFileBuilder OnInstall()
    {
        _onInstall = true;
        return this;
    }

    public RemoveFileBuilder OnUninstall()
    {
        _onUninstall = true;
        return this;
    }

    public RemoveFileBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    internal RemoveFileModel Build()
    {
        return new RemoveFileModel
        {
            Id = _id,
            DirectoryRef = _directory,
            FileName = _fileName,
            OnInstall = _onInstall,
            OnUninstall = _onUninstall,
            ComponentRef = _componentRef
        };
    }
}