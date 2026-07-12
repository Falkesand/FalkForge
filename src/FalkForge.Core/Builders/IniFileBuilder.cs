using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class IniFileBuilder
{
    private readonly string _fileName;
    private IniFileAction _action = IniFileAction.CreateEntry;
    private string? _dirProperty;
    private string _key = string.Empty;
    private string _section = string.Empty;
    private string _value = string.Empty;

    internal IniFileBuilder(string fileName)
    {
        _fileName = fileName;
    }

    /// <summary>
    /// Targets the INI file at a non-default directory via an MSI directory reference
    /// (e.g. an <c>InstallDir</c>-style property or Directory table key). When unset, the
    /// compiler resolves the file relative to the component it is authored under.
    /// </summary>
    public IniFileBuilder Directory(string dirProperty)
    {
        _dirProperty = dirProperty;
        return this;
    }

    public IniFileBuilder Section(string section)
    {
        _section = section;
        return this;
    }

    public IniFileBuilder Key(string key)
    {
        _key = key;
        return this;
    }

    public IniFileBuilder Value(string value)
    {
        _value = value;
        return this;
    }

    public IniFileBuilder Action(IniFileAction action)
    {
        _action = action;
        return this;
    }

    internal IniFileModel Build()
    {
        return new IniFileModel
        {
            FileName = _fileName,
            DirProperty = _dirProperty,
            Section = _section,
            Key = _key,
            Value = _value,
            Action = _action
        };
    }
}