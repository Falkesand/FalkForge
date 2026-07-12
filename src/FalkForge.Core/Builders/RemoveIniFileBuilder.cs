using FalkForge.Models;

namespace FalkForge.Builders;

/// <summary>
/// Fluent builder for <see cref="RemoveIniFileModel"/> — authors uninstall-time INI-file
/// removal entries for the MSI <c>RemoveIniFile</c> table.
/// </summary>
public sealed class RemoveIniFileBuilder
{
    private readonly string _fileName;
    private IniFileAction _action = IniFileAction.RemoveLine;
    private string? _componentRef;
    private string? _dirProperty;
    private string _id = string.Empty;
    private string _key = string.Empty;
    private string _section = string.Empty;
    private string? _value;

    internal RemoveIniFileBuilder(string fileName)
    {
        _fileName = fileName;
    }

    public RemoveIniFileBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public RemoveIniFileBuilder Directory(string dirProperty)
    {
        _dirProperty = dirProperty;
        return this;
    }

    public RemoveIniFileBuilder Section(string section)
    {
        _section = section;
        return this;
    }

    public RemoveIniFileBuilder Key(string key)
    {
        _key = key;
        return this;
    }

    public RemoveIniFileBuilder Value(string value)
    {
        _value = value;
        return this;
    }

    public RemoveIniFileBuilder Action(IniFileAction action)
    {
        _action = action;
        return this;
    }

    public RemoveIniFileBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    internal RemoveIniFileModel Build()
    {
        return new RemoveIniFileModel
        {
            Id = _id,
            FileName = _fileName,
            DirProperty = _dirProperty,
            Section = _section,
            Key = _key,
            Value = _value,
            Action = _action,
            ComponentRef = _componentRef
        };
    }
}
