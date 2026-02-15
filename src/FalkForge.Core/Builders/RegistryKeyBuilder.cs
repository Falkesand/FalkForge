namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class RegistryKeyBuilder
{
    private readonly RegistryRoot _root;
    private readonly string _key;
    private readonly List<RegistryEntryModel> _entries = [];

    internal RegistryKeyBuilder(RegistryRoot root, string key)
    {
        _root = root;
        _key = key;
    }

    public RegistryKeyBuilder Value(string name, string value, RegistryValueType type = RegistryValueType.String)
    {
        _entries.Add(new RegistryEntryModel { Root = _root, Key = _key, ValueName = name, Value = value, ValueType = type });
        return this;
    }

    public RegistryKeyBuilder DWord(string name, int value)
    {
        _entries.Add(new RegistryEntryModel { Root = _root, Key = _key, ValueName = name, Value = value, ValueType = RegistryValueType.DWord });
        return this;
    }

    public RegistryKeyBuilder DefaultValue(string value)
    {
        _entries.Add(new RegistryEntryModel { Root = _root, Key = _key, ValueName = null, Value = value, ValueType = RegistryValueType.String });
        return this;
    }

    internal IReadOnlyList<RegistryEntryModel> Build() => _entries;
}
