namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class FileAssociationBuilder
{
    private readonly string _extension;
    private string _progId = string.Empty;
    private readonly List<VerbModel> _verbs = [];

    internal FileAssociationBuilder(string extension) => _extension = extension;

    public string? Description { get; set; }
    public string? IconFile { get; set; }
    public int IconIndex { get; set; }
    public string? ContentType { get; set; }

    public FileAssociationBuilder ProgId(string progId)
    {
        _progId = progId;
        return this;
    }

    public FileAssociationBuilder Verb(string verb, string? argument = null, Action<VerbBuilder>? configure = null)
    {
        var builder = new VerbBuilder(verb) { Argument = argument };
        configure?.Invoke(builder);
        _verbs.Add(builder.Build());
        return this;
    }

    internal FileAssociationModel Build() => new()
    {
        Extension = _extension,
        ProgId = _progId,
        Description = Description,
        IconFile = IconFile,
        IconIndex = IconIndex,
        ContentType = ContentType,
        Verbs = _verbs
    };
}
