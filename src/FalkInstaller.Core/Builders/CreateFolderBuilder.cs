namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class CreateFolderBuilder
{
    private string _id = string.Empty;
    private string _directory = string.Empty;
    private string? _componentRef;

    public CreateFolderBuilder Id(string id) { _id = id; return this; }
    public CreateFolderBuilder Directory(string directory) { _directory = directory; return this; }
    public CreateFolderBuilder ComponentRef(string componentRef) { _componentRef = componentRef; return this; }

    internal CreateFolderModel Build() => new()
    {
        Id = _id,
        DirectoryRef = _directory,
        ComponentRef = _componentRef
    };
}
