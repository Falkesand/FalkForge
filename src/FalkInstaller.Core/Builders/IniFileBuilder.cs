namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class IniFileBuilder
{
    private readonly string _fileName;
    private string _section = string.Empty;
    private string _key = string.Empty;
    private string _value = string.Empty;
    private IniFileAction _action = IniFileAction.CreateEntry;

    internal IniFileBuilder(string fileName) => _fileName = fileName;

    public IniFileBuilder Section(string section) { _section = section; return this; }
    public IniFileBuilder Key(string key) { _key = key; return this; }
    public IniFileBuilder Value(string value) { _value = value; return this; }
    public IniFileBuilder Action(IniFileAction action) { _action = action; return this; }

    internal IniFileModel Build() => new()
    {
        FileName = _fileName,
        Section = _section,
        Key = _key,
        Value = _value,
        Action = _action
    };
}
