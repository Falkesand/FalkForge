using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class ComTypeLibBuilder
{
    private Guid _typeLibId;
    private Version _version = new(1, 0);
    private int _language;
    private string? _description;
    private string? _componentRef;

    public ComTypeLibBuilder TypeLibId(Guid id) { _typeLibId = id; return this; }
    public ComTypeLibBuilder Version(int major, int minor) { _version = new Version(major, minor); return this; }
    public ComTypeLibBuilder Language(int lcid) { _language = lcid; return this; }
    public ComTypeLibBuilder Description(string desc) { _description = desc; return this; }
    public ComTypeLibBuilder ComponentRef(string componentRef) { _componentRef = componentRef; return this; }

    public ComTypeLibModel Build() => new()
    {
        TypeLibId = _typeLibId,
        Version = _version,
        Language = _language,
        Description = _description,
        ComponentRef = _componentRef
    };
}
