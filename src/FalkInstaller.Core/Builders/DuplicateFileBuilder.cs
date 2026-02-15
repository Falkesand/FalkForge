namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class DuplicateFileBuilder
{
    private string _id = string.Empty;
    private string _fileRef = string.Empty;
    private string? _destDirectory;
    private string? _destFileName;
    private string? _componentRef;

    public DuplicateFileBuilder Id(string id) { _id = id; return this; }
    public DuplicateFileBuilder FileRef(string fileRef) { _fileRef = fileRef; return this; }
    public DuplicateFileBuilder DestDirectory(string directory) { _destDirectory = directory; return this; }
    public DuplicateFileBuilder DestFileName(string fileName) { _destFileName = fileName; return this; }
    public DuplicateFileBuilder ComponentRef(string componentRef) { _componentRef = componentRef; return this; }

    internal DuplicateFileModel Build() => new()
    {
        Id = _id,
        FileRef = _fileRef,
        DestDirectory = _destDirectory,
        DestFileName = _destFileName,
        ComponentRef = _componentRef
    };
}
