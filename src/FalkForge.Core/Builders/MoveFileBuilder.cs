namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class MoveFileBuilder
{
    private string _id = string.Empty;
    private string _sourceDirectory = string.Empty;
    private string _sourceFileName = string.Empty;
    private string _destDirectory = string.Empty;
    private string? _destFileName;
    private int _options = 1;
    private string? _componentRef;

    public MoveFileBuilder Id(string id) { _id = id; return this; }
    public MoveFileBuilder SourceDirectory(string directory) { _sourceDirectory = directory; return this; }
    public MoveFileBuilder SourceFileName(string fileName) { _sourceFileName = fileName; return this; }
    public MoveFileBuilder DestDirectory(string directory) { _destDirectory = directory; return this; }
    public MoveFileBuilder DestFileName(string fileName) { _destFileName = fileName; return this; }
    public MoveFileBuilder AsCopy() { _options = 0; return this; }
    public MoveFileBuilder AsMove() { _options = 1; return this; }
    public MoveFileBuilder ComponentRef(string componentRef) { _componentRef = componentRef; return this; }

    internal MoveFileModel Build() => new()
    {
        Id = _id,
        SourceDirectory = _sourceDirectory,
        SourceFileName = _sourceFileName,
        DestDirectory = _destDirectory,
        DestFileName = _destFileName,
        Options = _options,
        ComponentRef = _componentRef
    };
}
