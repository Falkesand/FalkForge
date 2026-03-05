using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class CreateFolderBuilder
{
    private string? _componentRef;
    private string _directory = string.Empty;
    private string _id = string.Empty;

    public CreateFolderBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public CreateFolderBuilder Directory(string directory)
    {
        _directory = directory;
        return this;
    }

    public CreateFolderBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    internal CreateFolderModel Build()
    {
        return new CreateFolderModel
        {
            Id = _id,
            DirectoryRef = _directory,
            ComponentRef = _componentRef
        };
    }
}