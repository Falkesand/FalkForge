using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class IniFileBuilder
{
    private readonly string _fileName;
    private IniFileAction _action = IniFileAction.CreateEntry;
    private string _key = string.Empty;
    private string _section = string.Empty;
    private string _value = string.Empty;

    internal IniFileBuilder(string fileName)
    {
        _fileName = fileName;
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
            Section = _section,
            Key = _key,
            Value = _value,
            Action = _action
        };
    }
}