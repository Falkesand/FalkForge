namespace FalkForge.Compiler.Msix.Builders;

public sealed class MsixApplicationBuilder
{
    private readonly List<MsixFileTypeAssociation> _fileTypeAssociations = [];
    private readonly List<MsixProtocol> _protocols = [];
    private readonly string _id;
    private readonly string _executable;
    private string? _entryPoint;
    private string? _displayName;
    private string? _description;
    private string _backgroundColor = "transparent";
    private string? _square150x150Logo;
    private string? _square44x44Logo;
    private string? _wide310x150Logo;

    public MsixApplicationBuilder(string id, string executable)
    {
        _id = id;
        _executable = executable;
    }

    public MsixApplicationBuilder EntryPoint(string entryPoint)
    {
        _entryPoint = entryPoint;
        return this;
    }

    public MsixApplicationBuilder DisplayName(string displayName)
    {
        _displayName = displayName;
        return this;
    }

    public MsixApplicationBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public MsixApplicationBuilder BackgroundColor(string color)
    {
        _backgroundColor = color;
        return this;
    }

    public MsixApplicationBuilder Square150x150Logo(string path)
    {
        _square150x150Logo = path;
        return this;
    }

    public MsixApplicationBuilder Square44x44Logo(string path)
    {
        _square44x44Logo = path;
        return this;
    }

    public MsixApplicationBuilder Wide310x150Logo(string path)
    {
        _wide310x150Logo = path;
        return this;
    }

    /// <summary>
    /// Registers a file type association. <paramref name="name"/> is the lowercase logical
    /// association name; each file type includes its leading dot (e.g. <c>.cdoc</c>).
    /// </summary>
    public MsixApplicationBuilder FileTypeAssociation(string name, params string[] fileTypes)
        => FileTypeAssociation(name, displayName: null, logo: null, fileTypes);

    /// <summary>
    /// Registers a file type association with a user-visible name and/or icon.
    /// </summary>
    public MsixApplicationBuilder FileTypeAssociation(
        string name, string? displayName, string? logo, IReadOnlyList<string> fileTypes)
    {
        _fileTypeAssociations.Add(new MsixFileTypeAssociation
        {
            Name = name,
            FileTypes = [.. fileTypes],
            DisplayName = displayName,
            Logo = logo
        });
        return this;
    }

    /// <summary>
    /// Registers a URI scheme handled by this application. <paramref name="name"/> is the
    /// scheme alone — <c>contoso</c>, not <c>contoso://</c>.
    /// </summary>
    public MsixApplicationBuilder Protocol(string name, string? displayName = null, string? logo = null)
    {
        _protocols.Add(new MsixProtocol
        {
            Name = name,
            DisplayName = displayName,
            Logo = logo
        });
        return this;
    }

    internal MsixApplication Build() => new()
    {
        Id = _id,
        Executable = _executable,
        EntryPoint = _entryPoint,
        FileTypeAssociations = [.. _fileTypeAssociations],
        Protocols = [.. _protocols],
        VisualElements = new MsixVisualElements
        {
            DisplayName = _displayName ?? _id,
            Description = _description,
            BackgroundColor = _backgroundColor,
            Square150x150Logo = _square150x150Logo,
            Square44x44Logo = _square44x44Logo,
            Wide310x150Logo = _wide310x150Logo
        }
    };
}
